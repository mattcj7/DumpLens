using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DumpLens.Application.Audit;
using DumpLens.Application.Identities;
using DumpLens.Application.Imports;
using DumpLens.Application.MessageImports;
using DumpLens.Application.Timestamps;
using DumpLens.Persistence.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpLens.Persistence.MessageImports;

public sealed class SqliteMessageImportService : IMessageImportService
{
    private const string AuditActionType = "messages_imported";
    private const string AuditEntityType = "source_import";
    private const int BatchSize = 200;
    private const string IdentityCreatedBy = "system";
    private const string ImportStatusImported = "imported";
    private const string MessageArtifactType = "message_row";
    private const string OperationName = "message_import";
    private const string RecipientRole = "recipient";
    private const string WarningSeverity = "warning";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Func<string, IAuditLogger> _auditLoggerFactory;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly IReadOnlyDictionary<ImportSourceKind, ISourceImporter> _importers;
    private readonly ILogger<SqliteMessageImportService> _logger;
    private readonly ITimestampNormalizer _timestampNormalizer;

    public SqliteMessageImportService(
        IEnumerable<ISourceImporter> importers,
        IIdentityNormalizer identityNormalizer,
        ITimestampNormalizer timestampNormalizer,
        Func<string, IAuditLogger>? auditLoggerFactory = null,
        ILogger<SqliteMessageImportService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(importers);

        _importers = importers.ToDictionary(static importer => importer.SourceKind);
        _identityNormalizer = identityNormalizer ?? throw new ArgumentNullException(nameof(identityNormalizer));
        _timestampNormalizer = timestampNormalizer ?? throw new ArgumentNullException(nameof(timestampNormalizer));
        _auditLoggerFactory = auditLoggerFactory ?? (connectionString => new SqliteAuditLogger(connectionString));
        _logger = logger ?? NullLogger<SqliteMessageImportService>.Instance;
    }

    public async Task<ImportMessagesResult> ImportAsync(
        ImportMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = ValidateAndNormalize(request);
        var connectionString = BuildConnectionString(normalizedRequest.CaseDatabasePath);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var failureStage = "validation";
        string? auditEventId = null;
        var safeSourceFileExtension = TryGetFileExtension(normalizedRequest.SourceFilePath);

        _logger.LogInformation(
            "Message import started. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} source_kind={SourceKind} source_file_extension={SourceFileExtension} worksheet_name_present={WorksheetNamePresent} row_limit={RowLimit}",
            OperationName,
            normalizedRequest.CorrelationId,
            normalizedRequest.CaseId,
            normalizedRequest.SourceImportId,
            normalizedRequest.SourceKind,
            safeSourceFileExtension,
            !string.IsNullOrWhiteSpace(normalizedRequest.WorksheetName),
            normalizedRequest.RowLimit);

        try
        {
            failureStage = "source_validation";
            var sourceImport = await LoadSourceImportAsync(connectionString, normalizedRequest.SourceImportId, cancellationToken)
                .ConfigureAwait(false);
            if (sourceImport is null || !string.Equals(sourceImport.CaseId, normalizedRequest.CaseId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The requested source_import_id was not found for the target case.");
            }

            var sourceFilePath = ResolveSourceFilePath(normalizedRequest, sourceImport);
            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException("The requested source file path could not be found.", sourceFilePath);
            }

            var importer = ResolveImporter(normalizedRequest.SourceKind, sourceFilePath);

            _logger.LogInformation(
                "Source validation completed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} source_kind={SourceKind} source_import_status={SourceImportStatus}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                normalizedRequest.SourceKind,
                sourceImport.ImportStatus);

            failureStage = "row_read";
            var readResult = await importer.ReadTabularDataAsync(
                    new ImportTabularDataRequest
                    {
                        FilePath = sourceFilePath,
                        WorksheetName = normalizedRequest.WorksheetName,
                        RowLimit = normalizedRequest.RowLimit,
                        CorrelationId = normalizedRequest.CorrelationId
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (!readResult.IsSupported || !readResult.IsTabular)
            {
                throw new InvalidOperationException("The requested source could not be read as supported tabular message data.");
            }

            _logger.LogInformation(
                "Rows parsed/read. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} rows_read={RowsRead} importer_warning_count={ImporterWarningCount} has_header_row={HasHeaderRow}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                readResult.Rows.Count,
                readResult.Warnings.Count,
                readResult.HasHeaderRow);

            failureStage = "persistence";
            var completedAtUtc = default(DateTimeOffset);
            int importedMessageCount;
            int sourceArtifactCount;
            int recipientCount;
            int warningCount;
            int identityCountCreated;
            int identityCountReused;

            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                var importState = new ImportState();
                await StageGlobalWarningsAsync(importState, normalizedRequest, readResult, sourceImport, cancellationToken).ConfigureAwait(false);
                await ProcessRowsAsync(connection, transaction, normalizedRequest, readResult, sourceImport, importState, cancellationToken)
                    .ConfigureAwait(false);

                identityCountCreated = importState.CreatedIdentityIds.Count;
                identityCountReused = importState.ReusedIdentityIds.Count;

                _logger.LogInformation(
                    "Identities created/reused. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} identity_count_created={IdentityCountCreated} identity_count_reused={IdentityCountReused}",
                    OperationName,
                    normalizedRequest.CorrelationId,
                    normalizedRequest.CaseId,
                    normalizedRequest.SourceImportId,
                    identityCountCreated,
                    identityCountReused);

                await InsertSourceArtifactsAsync(connection, transaction, importState.SourceArtifacts, normalizedRequest, cancellationToken)
                    .ConfigureAwait(false);
                sourceArtifactCount = importState.SourceArtifacts.Count;

                await InsertMessagesAsync(connection, transaction, importState.Messages, normalizedRequest, cancellationToken)
                    .ConfigureAwait(false);
                importedMessageCount = importState.Messages.Count;
                recipientCount = importState.MessageRecipients.Count;

                _logger.LogInformation(
                    "Messages inserted. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} imported_message_count={ImportedMessageCount} recipient_count={RecipientCount}",
                    OperationName,
                    normalizedRequest.CorrelationId,
                    normalizedRequest.CaseId,
                    normalizedRequest.SourceImportId,
                    importedMessageCount,
                    recipientCount);

                await InsertMessageRecipientsAsync(connection, transaction, importState.MessageRecipients, normalizedRequest, cancellationToken)
                    .ConfigureAwait(false);

                await InsertWarningsAsync(connection, transaction, importState.Warnings, normalizedRequest, cancellationToken)
                    .ConfigureAwait(false);
                warningCount = importState.Warnings.Count;

                _logger.LogInformation(
                    "Warnings inserted. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} warning_count={WarningCount}",
                    OperationName,
                    normalizedRequest.CorrelationId,
                    normalizedRequest.CaseId,
                    normalizedRequest.SourceImportId,
                    warningCount);

                completedAtUtc = DateTimeOffset.UtcNow;
                await UpdateSourceImportCountsAsync(
                        connection,
                        transaction,
                        normalizedRequest.SourceImportId,
                        importedMessageCount,
                        warningCount,
                        completedAtUtc,
                        cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Source imports counts updated. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} record_count={RecordCount} warning_count={WarningCount}",
                    OperationName,
                    normalizedRequest.CorrelationId,
                    normalizedRequest.CaseId,
                    normalizedRequest.SourceImportId,
                    importedMessageCount,
                    warningCount);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            failureStage = "audit_event_write";
            var auditWrite = await _auditLoggerFactory(connectionString).WriteAsync(
                    new AuditEventDraft
                    {
                        CaseId = normalizedRequest.CaseId,
                        UserId = normalizedRequest.ImportedByUserId,
                        ActionType = AuditActionType,
                        EntityType = AuditEntityType,
                        EntityId = normalizedRequest.SourceImportId,
                        Summary = "Message import completed.",
                        NewValueJson = CreateAuditNewValueJson(
                            normalizedRequest,
                            importedMessageCount,
                            sourceArtifactCount,
                            identityCountCreated,
                            identityCountReused,
                            recipientCount,
                            warningCount,
                            startedAtUtc,
                            completedAtUtc),
                        EventTimeUtc = completedAtUtc,
                        CorrelationId = normalizedRequest.CorrelationId
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            auditEventId = auditWrite.AuditEvent.Id;

            _logger.LogInformation(
                "Message import audit event written. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} audit_event_id={AuditEventId}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                auditEventId);

            _logger.LogInformation(
                "Message import completed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} imported_message_count={ImportedMessageCount} source_artifact_count={SourceArtifactCount} warning_count={WarningCount}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                importedMessageCount,
                sourceArtifactCount,
                warningCount);

            return new ImportMessagesResult
            {
                CaseId = normalizedRequest.CaseId,
                SourceImportId = normalizedRequest.SourceImportId,
                ImportedMessageCount = importedMessageCount,
                SourceArtifactCount = sourceArtifactCount,
                IdentityCountCreated = identityCountCreated,
                IdentityCountReused = identityCountReused,
                RecipientCount = recipientCount,
                WarningCount = warningCount,
                AuditEventId = auditEventId,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Message import failed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} failure_stage={FailureStage} failure_type={FailureType}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                failureStage,
                exception.GetType().Name);
            throw;
        }
    }

    private Task StageGlobalWarningsAsync(
        ImportState importState,
        NormalizedImportRequest request,
        ImportTabularDataResult readResult,
        SourceImportLookup sourceImport,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var fieldName in GetRequiredMappingNames())
        {
            if (!TryGetFieldMapping(request.FieldMappings, fieldName, out _))
            {
                importState.Warnings.Add(CreateWarning(
                    request.CaseId,
                    request.SourceImportId,
                    artifactId: null,
                    MessageImportWarningCodes.MissingRequiredMapping,
                    $"The required '{fieldName}' field mapping was not provided.",
                    fieldName,
                    rawValue: null));
            }
        }

        foreach (var warning in readResult.Warnings)
        {
            var rowLocator = BuildArtifactLocator(warning.WorksheetName ?? readResult.SelectedWorksheetName, warning.RowNumber);
            importState.PendingImporterWarnings.Add(new PendingImporterWarning(warning, rowLocator));
        }

        return Task.CompletedTask;
    }

    private async Task ProcessRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedImportRequest request,
        ImportTabularDataResult readResult,
        SourceImportLookup sourceImport,
        ImportState importState,
        CancellationToken cancellationToken)
    {
        foreach (var row in readResult.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var artifactId = Guid.NewGuid().ToString("N");
            var artifactLocator = BuildArtifactLocator(readResult.SelectedWorksheetName, row.RowNumber);
            importState.SourceArtifactsByLocator[artifactLocator] = artifactId;

            var columnValues = BuildColumnValues(readResult.Columns, row.Values);
            var mappedValues = ResolveMappedValues(request.FieldMappings, readResult.Columns, row.Values);
            var artifactMetadataJson = CreateSourceArtifactMetadataJson(
                request.SourceKind,
                readResult.SelectedWorksheetName,
                row.RowNumber,
                columnValues,
                mappedValues);

            importState.SourceArtifacts.Add(new SourceArtifactRecord(
                Id: artifactId,
                CaseId: request.CaseId,
                SourceImportId: request.SourceImportId,
                ArtifactType: MessageArtifactType,
                ArtifactLocator: artifactLocator,
                RowNumber: row.RowNumber,
                ObjectPath: readResult.SelectedWorksheetName,
                ProviderObjectId: NormalizeOptional(mappedValues.GetValueOrDefault(ImportFieldNames.MessageId)),
                RawMetadataJson: artifactMetadataJson,
                CreatedAtUtc: DateTimeOffset.UtcNow));

            try
            {
                var senderValue = NormalizeOptional(mappedValues.GetValueOrDefault(ImportFieldNames.Sender));
                var recipientValue = NormalizeOptional(mappedValues.GetValueOrDefault(ImportFieldNames.Recipient));
                var bodyValue = mappedValues.GetValueOrDefault(ImportFieldNames.MessageBody);
                var timestampValue = mappedValues.GetValueOrDefault(ImportFieldNames.Timestamp);
                var platformValue = ResolvePlatform(mappedValues.GetValueOrDefault(ImportFieldNames.Platform), request.DefaultPlatform, sourceImport.Platform);
                var directionValue = NormalizeOptional(mappedValues.GetValueOrDefault(ImportFieldNames.Direction))?.ToLowerInvariant();
                var threadIdValue = NormalizeOptional(mappedValues.GetValueOrDefault(ImportFieldNames.ThreadId));
                var messageIdValue = NormalizeOptional(mappedValues.GetValueOrDefault(ImportFieldNames.MessageId));
                var attachmentValue = NormalizeOptional(mappedValues.GetValueOrDefault(ImportFieldNames.Attachment));

                if (senderValue is null)
                {
                    importState.Warnings.Add(CreateWarning(
                        request.CaseId,
                        request.SourceImportId,
                        artifactId,
                        MessageImportWarningCodes.MissingSender,
                        "The sender value is missing for this row.",
                        ImportFieldNames.Sender,
                        mappedValues.GetValueOrDefault(ImportFieldNames.Sender)));
                }

                if (recipientValue is null)
                {
                    importState.Warnings.Add(CreateWarning(
                        request.CaseId,
                        request.SourceImportId,
                        artifactId,
                        MessageImportWarningCodes.MissingRecipient,
                        "The recipient value is missing for this row.",
                        ImportFieldNames.Recipient,
                        mappedValues.GetValueOrDefault(ImportFieldNames.Recipient)));
                }

                if (string.IsNullOrWhiteSpace(bodyValue))
                {
                    importState.Warnings.Add(CreateWarning(
                        request.CaseId,
                        request.SourceImportId,
                        artifactId,
                        MessageImportWarningCodes.MissingMessageBody,
                        "The message body is missing for this row.",
                        ImportFieldNames.MessageBody,
                        bodyValue));
                }

                IdentityResolution? senderIdentity = null;
                if (senderValue is not null)
                {
                    senderIdentity = await ResolveIdentityAsync(
                            connection,
                            transaction,
                            request,
                            sourceImport,
                            senderValue,
                            artifactId,
                            artifactLocator,
                            ImportFieldNames.Sender,
                            isSender: true,
                            importState,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                var recipientInputs = SplitRecipientValues(recipientValue, artifactId, request, importState);
                var recipientResolutions = new List<IdentityResolution>(recipientInputs.Count);
                foreach (var recipientInput in recipientInputs)
                {
                    var recipientIdentity = await ResolveIdentityAsync(
                            connection,
                            transaction,
                            request,
                            sourceImport,
                            recipientInput,
                            artifactId,
                            artifactLocator,
                            ImportFieldNames.Recipient,
                            isSender: false,
                            importState,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (recipientIdentity is not null)
                    {
                        recipientResolutions.Add(recipientIdentity);
                    }
                }

                var timestampResult = NormalizeTimestamp(request, timestampValue, artifactId, importState);
                if (!string.IsNullOrWhiteSpace(messageIdValue) &&
                    !importState.SeenProviderMessageIds.Add(messageIdValue))
                {
                    importState.Warnings.Add(CreateWarning(
                        request.CaseId,
                        request.SourceImportId,
                        artifactId,
                        MessageImportWarningCodes.DuplicateProviderMessageId,
                        "The provider message ID was already observed in this import.",
                        ImportFieldNames.MessageId,
                        messageIdValue));
                }

                if (!string.IsNullOrWhiteSpace(attachmentValue))
                {
                    importState.Warnings.Add(CreateWarning(
                        request.CaseId,
                        request.SourceImportId,
                        artifactId,
                        MessageImportWarningCodes.AttachmentNotPersisted,
                        "Attachment metadata was detected, but attachment persistence is out of scope for this ticket.",
                        ImportFieldNames.Attachment,
                        attachmentValue));
                }

                if (platformValue is null)
                {
                    importState.Warnings.Add(CreateWarning(
                        request.CaseId,
                        request.SourceImportId,
                        artifactId,
                        MessageImportWarningCodes.UnknownPlatform,
                        "No platform value was available from the field mapping, request default, or registered source metadata.",
                        ImportFieldNames.Platform,
                        mappedValues.GetValueOrDefault(ImportFieldNames.Platform)));
                }

                var createdAtUtc = DateTimeOffset.UtcNow;
                var messageId = Guid.NewGuid().ToString("N");
                var recipientIdentityIds = recipientResolutions
                    .Select(static resolution => resolution.IdentityId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                importState.Messages.Add(new MessageRecord(
                    Id: messageId,
                    CaseId: request.CaseId,
                    SourceImportId: request.SourceImportId,
                    SourceArtifactId: artifactId,
                    Platform: platformValue,
                    SourceThreadId: threadIdValue,
                    ProviderMessageId: messageIdValue,
                    EventTimeOriginal: timestampValue,
                    EventTimeUtc: timestampResult.NormalizedUtc,
                    Timezone: timestampResult.ResolvedTimezone,
                    SenderIdentityId: senderIdentity?.IdentityId,
                    Direction: directionValue,
                    MessageBody: NormalizeStoredText(bodyValue),
                    MessageBodyNormalized: NormalizeMessageBody(bodyValue),
                    MessageBodySha256: ComputeBodySha256(bodyValue),
                    HasAttachments: !string.IsNullOrWhiteSpace(attachmentValue),
                    ImportConfidence: DetermineImportConfidence(timestampResult.Confidence, senderIdentity, recipientResolutions, bodyValue),
                    OriginalMetadataJson: CreateMessageMetadataJson(
                        request.SourceKind,
                        readResult.SelectedWorksheetName,
                        row.RowNumber,
                        artifactLocator,
                        mappedValues,
                        senderIdentity,
                        recipientIdentityIds.Length,
                        timestampResult.Confidence),
                    CreatedAtUtc: createdAtUtc,
                    UpdatedAtUtc: createdAtUtc));

                foreach (var recipientIdentityId in recipientIdentityIds)
                {
                    importState.MessageRecipients.Add(new MessageRecipientRecord(
                        Id: Guid.NewGuid().ToString("N"),
                        CaseId: request.CaseId,
                        MessageId: messageId,
                        RecipientIdentityId: recipientIdentityId,
                        RecipientRole: RecipientRole,
                        CreatedAtUtc: createdAtUtc));
                }
            }
            catch (Exception)
            {
                importState.Warnings.Add(CreateWarning(
                    request.CaseId,
                    request.SourceImportId,
                    artifactId,
                    MessageImportWarningCodes.RowImportFailed,
                    "The row could not be imported as a message record.",
                    fieldName: null,
                    rawValue: null));
            }
        }

        foreach (var pendingWarning in importState.PendingImporterWarnings)
        {
            importState.Warnings.Add(CreateImporterWarningRecord(request, readResult, pendingWarning, importState.SourceArtifactsByLocator));
        }
    }

    private TimestampImportResolution NormalizeTimestamp(
        NormalizedImportRequest request,
        string? timestampValue,
        string artifactId,
        ImportState importState)
    {
        var trimmedTimestamp = NormalizeOptional(timestampValue);
        if (trimmedTimestamp is null)
        {
            importState.Warnings.Add(CreateWarning(
                request.CaseId,
                request.SourceImportId,
                artifactId,
                MessageImportWarningCodes.MissingTimestamp,
                "The timestamp value is missing for this row.",
                ImportFieldNames.Timestamp,
                timestampValue));

            return new TimestampImportResolution(null, NormalizeOptional(request.TimezoneAssumption), "unknown");
        }

        var normalizedTimestamp = _timestampNormalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = timestampValue,
            TimezoneAssumption = request.TimezoneAssumption
        });

        foreach (var warning in normalizedTimestamp.Warnings)
        {
            importState.Warnings.Add(CreateWarning(
                request.CaseId,
                request.SourceImportId,
                artifactId,
                warning.Code == TimestampNormalizeWarningCodes.EmptyValue
                    ? MessageImportWarningCodes.MissingTimestamp
                    : MessageImportWarningCodes.InvalidTimestamp,
                warning.Message,
                ImportFieldNames.Timestamp,
                timestampValue));
        }

        return new TimestampImportResolution(
            normalizedTimestamp.NormalizedUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            normalizedTimestamp.ResolvedTimezoneId ?? NormalizeOptional(request.TimezoneAssumption),
            normalizedTimestamp.Confidence);
    }

    private async Task<IdentityResolution?> ResolveIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedImportRequest request,
        SourceImportLookup sourceImport,
        string rawValue,
        string artifactId,
        string artifactLocator,
        string fieldName,
        bool isSender,
        ImportState importState,
        CancellationToken cancellationToken)
    {
        var identityType = InferIdentityType(rawValue, sourceImport.Platform, request.DefaultPlatform);
        var normalization = _identityNormalizer.Normalize(new IdentityNormalizeRequest
        {
            IdentityType = identityType,
            RawValue = rawValue,
            DisplayValue = rawValue
        });

        foreach (var warning in normalization.Warnings)
        {
            importState.Warnings.Add(CreateWarning(
                request.CaseId,
                request.SourceImportId,
                artifactId,
                ClassifyIdentityWarningCode(warning.Code, isSender),
                warning.Message,
                fieldName,
                rawValue));
        }

        var cacheKey = CreateIdentityCacheKey(identityType, normalization, rawValue);
        if (importState.IdentityCache.TryGetValue(cacheKey, out var cachedIdentity))
        {
            importState.ReusedIdentityIds.Add(cachedIdentity.Id);
            return new IdentityResolution(cachedIdentity.Id, identityType, normalization.Confidence);
        }

        var storedIdentity = await FindExistingIdentityAsync(connection, transaction, request.CaseId, identityType, normalization, rawValue, cancellationToken)
            .ConfigureAwait(false);
        if (storedIdentity is not null)
        {
            importState.IdentityCache[cacheKey] = storedIdentity;
            importState.ReusedIdentityIds.Add(storedIdentity.Id);
            return new IdentityResolution(storedIdentity.Id, identityType, normalization.Confidence);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var newIdentity = new IdentityRecord(
            Id: Guid.NewGuid().ToString("N"),
            CaseId: request.CaseId,
            IdentityType: identityType,
            RawValue: rawValue,
            NormalizedValue: NormalizeNullableForStorage(normalization.NormalizedValue),
            DisplayValue: NormalizeNullableForStorage(normalization.DisplayValue),
            SourceImportId: request.SourceImportId,
            Platform: ResolvePlatform(sourceImport.Platform, request.DefaultPlatform, sourceImport.Platform),
            Confidence: normalization.Confidence,
            CreatedAtUtc: nowUtc,
            UpdatedAtUtc: nowUtc);

        await InsertIdentityAsync(connection, transaction, newIdentity, cancellationToken).ConfigureAwait(false);

        importState.IdentityCache[cacheKey] = newIdentity;
        importState.CreatedIdentityIds.Add(newIdentity.Id);

        return new IdentityResolution(newIdentity.Id, identityType, normalization.Confidence);
    }

    private static List<string> SplitRecipientValues(
        string? rawRecipientValue,
        string artifactId,
        NormalizedImportRequest request,
        ImportState importState)
    {
        var trimmedRecipientValue = NormalizeOptional(rawRecipientValue);
        if (trimmedRecipientValue is null)
        {
            return [];
        }

        var segments = trimmedRecipientValue
            .Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (segments.Count <= 1)
        {
            return [trimmedRecipientValue];
        }

        importState.Warnings.Add(CreateWarning(
            request.CaseId,
            request.SourceImportId,
            artifactId,
            MessageImportWarningCodes.MultipleRecipientsSplit,
            "The recipient field contained multiple delimited values and was split into multiple recipients.",
            ImportFieldNames.Recipient,
            rawRecipientValue));

        return segments;
    }

    private static async Task<SourceImportLookup?> LoadSourceImportAsync(
        string connectionString,
        string sourceImportId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                case_id,
                source_name,
                source_type,
                platform,
                stored_file_path,
                import_status
            FROM source_imports
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", sourceImportId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new SourceImportLookup(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6));
    }

    private static async Task<IdentityRecord?> FindExistingIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string caseId,
        string identityType,
        IdentityNormalizeResult normalization,
        string rawValue,
        CancellationToken cancellationToken)
    {
        var normalizedValue = NormalizeNullableForStorage(normalization.NormalizedValue);
        if (normalizedValue is not null)
        {
            await using var normalizedCommand = connection.CreateCommand();
            normalizedCommand.Transaction = transaction;
            normalizedCommand.CommandText =
                """
                SELECT
                    id,
                    case_id,
                    identity_type,
                    raw_value,
                    normalized_value,
                    display_value,
                    source_import_id,
                    platform,
                    confidence,
                    created_at_utc,
                    updated_at_utc
                FROM identities
                WHERE case_id = $caseId
                  AND identity_type = $identityType
                  AND normalized_value = $normalizedValue
                ORDER BY created_at_utc ASC, id ASC
                LIMIT 1;
                """;
            normalizedCommand.Parameters.AddWithValue("$caseId", caseId);
            normalizedCommand.Parameters.AddWithValue("$identityType", identityType);
            normalizedCommand.Parameters.AddWithValue("$normalizedValue", normalizedValue);

            var existingByNormalized = await ReadIdentityAsync(normalizedCommand, cancellationToken).ConfigureAwait(false);
            if (existingByNormalized is not null)
            {
                return existingByNormalized;
            }
        }

        var fallbackValue = NormalizeNullableForStorage(normalization.DisplayValue) ?? NormalizeOptional(rawValue);
        if (fallbackValue is null)
        {
            return null;
        }

        await using var fallbackCommand = connection.CreateCommand();
        fallbackCommand.Transaction = transaction;
        fallbackCommand.CommandText =
            """
            SELECT
                id,
                case_id,
                identity_type,
                raw_value,
                normalized_value,
                display_value,
                source_import_id,
                platform,
                confidence,
                created_at_utc,
                updated_at_utc
            FROM identities
            WHERE case_id = $caseId
              AND identity_type = $identityType
              AND (
                    display_value = $fallbackValue
                    OR raw_value = $fallbackValue
                  )
            ORDER BY created_at_utc ASC, id ASC
            LIMIT 1;
            """;
        fallbackCommand.Parameters.AddWithValue("$caseId", caseId);
        fallbackCommand.Parameters.AddWithValue("$identityType", identityType);
        fallbackCommand.Parameters.AddWithValue("$fallbackValue", fallbackValue);

        return await ReadIdentityAsync(fallbackCommand, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IdentityRecord?> ReadIdentityAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new IdentityRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    private static async Task InsertIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IdentityRecord identity,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO identities (
                id,
                case_id,
                identity_type,
                raw_value,
                normalized_value,
                display_value,
                linked_person_id,
                source_import_id,
                source_artifact_id,
                platform,
                confidence,
                review_status,
                created_by,
                notes,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                $caseId,
                $identityType,
                $rawValue,
                $normalizedValue,
                $displayValue,
                NULL,
                $sourceImportId,
                NULL,
                $platform,
                $confidence,
                'unreviewed',
                $createdBy,
                NULL,
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", identity.Id);
        command.Parameters.AddWithValue("$caseId", identity.CaseId);
        command.Parameters.AddWithValue("$identityType", identity.IdentityType);
        command.Parameters.AddWithValue("$rawValue", identity.RawValue);
        command.Parameters.AddWithValue("$normalizedValue", ToSqlValue(identity.NormalizedValue));
        command.Parameters.AddWithValue("$displayValue", ToSqlValue(identity.DisplayValue));
        command.Parameters.AddWithValue("$sourceImportId", ToSqlValue(identity.SourceImportId));
        command.Parameters.AddWithValue("$platform", ToSqlValue(identity.Platform));
        command.Parameters.AddWithValue("$confidence", identity.Confidence);
        command.Parameters.AddWithValue("$createdBy", IdentityCreatedBy);
        command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(identity.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(identity.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertSourceArtifactsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<SourceArtifactRecord> records,
        NormalizedImportRequest request,
        CancellationToken cancellationToken)
    {
        await InsertInBatchesAsync(
            records,
            "source_artifacts",
            request,
            async batch =>
            {
                foreach (var record in batch)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText =
                        """
                        INSERT INTO source_artifacts (
                            id,
                            case_id,
                            source_import_id,
                            artifact_type,
                            artifact_locator,
                            row_number,
                            page_number,
                            object_path,
                            provider_object_id,
                            artifact_hash,
                            raw_text,
                            raw_metadata_json,
                            created_at_utc
                        )
                        VALUES (
                            $id,
                            $caseId,
                            $sourceImportId,
                            $artifactType,
                            $artifactLocator,
                            $rowNumber,
                            NULL,
                            $objectPath,
                            $providerObjectId,
                            NULL,
                            NULL,
                            $rawMetadataJson,
                            $createdAtUtc
                        );
                        """;
                    command.Parameters.AddWithValue("$id", record.Id);
                    command.Parameters.AddWithValue("$caseId", record.CaseId);
                    command.Parameters.AddWithValue("$sourceImportId", record.SourceImportId);
                    command.Parameters.AddWithValue("$artifactType", record.ArtifactType);
                    command.Parameters.AddWithValue("$artifactLocator", ToSqlValue(record.ArtifactLocator));
                    command.Parameters.AddWithValue("$rowNumber", record.RowNumber);
                    command.Parameters.AddWithValue("$objectPath", ToSqlValue(record.ObjectPath));
                    command.Parameters.AddWithValue("$providerObjectId", ToSqlValue(record.ProviderObjectId));
                    command.Parameters.AddWithValue("$rawMetadataJson", record.RawMetadataJson);
                    command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(record.CreatedAtUtc));
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertMessagesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<MessageRecord> records,
        NormalizedImportRequest request,
        CancellationToken cancellationToken)
    {
        await InsertInBatchesAsync(
            records,
            "messages",
            request,
            async batch =>
            {
                foreach (var record in batch)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText =
                        """
                        INSERT INTO messages (
                            id,
                            case_id,
                            source_import_id,
                            source_artifact_id,
                            platform,
                            source_thread_id,
                            provider_message_id,
                            conversation_id,
                            event_time_original,
                            event_time_utc,
                            timezone,
                            sender_identity_id,
                            direction,
                            message_body,
                            message_body_normalized,
                            message_body_sha256,
                            has_attachments,
                            deleted_status,
                            read_status,
                            import_confidence,
                            reconciliation_status,
                            review_status,
                            original_metadata_json,
                            created_at_utc,
                            updated_at_utc
                        )
                        VALUES (
                            $id,
                            $caseId,
                            $sourceImportId,
                            $sourceArtifactId,
                            $platform,
                            $sourceThreadId,
                            $providerMessageId,
                            NULL,
                            $eventTimeOriginal,
                            $eventTimeUtc,
                            $timezone,
                            $senderIdentityId,
                            $direction,
                            $messageBody,
                            $messageBodyNormalized,
                            $messageBodySha256,
                            $hasAttachments,
                            'present',
                            NULL,
                            $importConfidence,
                            'unmatched',
                            'unreviewed',
                            $originalMetadataJson,
                            $createdAtUtc,
                            $updatedAtUtc
                        );
                        """;
                    command.Parameters.AddWithValue("$id", record.Id);
                    command.Parameters.AddWithValue("$caseId", record.CaseId);
                    command.Parameters.AddWithValue("$sourceImportId", record.SourceImportId);
                    command.Parameters.AddWithValue("$sourceArtifactId", ToSqlValue(record.SourceArtifactId));
                    command.Parameters.AddWithValue("$platform", ToSqlValue(record.Platform));
                    command.Parameters.AddWithValue("$sourceThreadId", ToSqlValue(record.SourceThreadId));
                    command.Parameters.AddWithValue("$providerMessageId", ToSqlValue(record.ProviderMessageId));
                    command.Parameters.AddWithValue("$eventTimeOriginal", ToSqlValue(record.EventTimeOriginal));
                    command.Parameters.AddWithValue("$eventTimeUtc", ToSqlValue(record.EventTimeUtc));
                    command.Parameters.AddWithValue("$timezone", ToSqlValue(record.Timezone));
                    command.Parameters.AddWithValue("$senderIdentityId", ToSqlValue(record.SenderIdentityId));
                    command.Parameters.AddWithValue("$direction", ToSqlValue(record.Direction));
                    command.Parameters.AddWithValue("$messageBody", ToSqlValue(record.MessageBody));
                    command.Parameters.AddWithValue("$messageBodyNormalized", ToSqlValue(record.MessageBodyNormalized));
                    command.Parameters.AddWithValue("$messageBodySha256", ToSqlValue(record.MessageBodySha256));
                    command.Parameters.AddWithValue("$hasAttachments", record.HasAttachments ? 1 : 0);
                    command.Parameters.AddWithValue("$importConfidence", record.ImportConfidence);
                    command.Parameters.AddWithValue("$originalMetadataJson", record.OriginalMetadataJson);
                    command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(record.CreatedAtUtc));
                    command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(record.UpdatedAtUtc));
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertMessageRecipientsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<MessageRecipientRecord> records,
        NormalizedImportRequest request,
        CancellationToken cancellationToken)
    {
        await InsertInBatchesAsync(
            records,
            "message_recipients",
            request,
            async batch =>
            {
                foreach (var record in batch)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText =
                        """
                        INSERT OR IGNORE INTO message_recipients (
                            id,
                            case_id,
                            message_id,
                            recipient_identity_id,
                            recipient_role,
                            created_at_utc
                        )
                        VALUES (
                            $id,
                            $caseId,
                            $messageId,
                            $recipientIdentityId,
                            $recipientRole,
                            $createdAtUtc
                        );
                        """;
                    command.Parameters.AddWithValue("$id", record.Id);
                    command.Parameters.AddWithValue("$caseId", record.CaseId);
                    command.Parameters.AddWithValue("$messageId", record.MessageId);
                    command.Parameters.AddWithValue("$recipientIdentityId", record.RecipientIdentityId);
                    command.Parameters.AddWithValue("$recipientRole", record.RecipientRole);
                    command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(record.CreatedAtUtc));
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertWarningsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<ImportWarningRecord> records,
        NormalizedImportRequest request,
        CancellationToken cancellationToken)
    {
        await InsertInBatchesAsync(
            records,
            "import_warnings",
            request,
            async batch =>
            {
                foreach (var record in batch)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText =
                        """
                        INSERT INTO import_warnings (
                            id,
                            case_id,
                            source_import_id,
                            artifact_id,
                            severity,
                            warning_code,
                            message,
                            field_name,
                            raw_value,
                            resolved_status,
                            resolved_by_user_id,
                            resolved_at_utc,
                            created_at_utc
                        )
                        VALUES (
                            $id,
                            $caseId,
                            $sourceImportId,
                            $artifactId,
                            $severity,
                            $warningCode,
                            $message,
                            $fieldName,
                            $rawValue,
                            'open',
                            NULL,
                            NULL,
                            $createdAtUtc
                        );
                        """;
                    command.Parameters.AddWithValue("$id", record.Id);
                    command.Parameters.AddWithValue("$caseId", record.CaseId);
                    command.Parameters.AddWithValue("$sourceImportId", record.SourceImportId);
                    command.Parameters.AddWithValue("$artifactId", ToSqlValue(record.ArtifactId));
                    command.Parameters.AddWithValue("$severity", record.Severity);
                    command.Parameters.AddWithValue("$warningCode", record.WarningCode);
                    command.Parameters.AddWithValue("$message", record.Message);
                    command.Parameters.AddWithValue("$fieldName", ToSqlValue(record.FieldName));
                    command.Parameters.AddWithValue("$rawValue", ToSqlValue(record.RawValue));
                    command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(record.CreatedAtUtc));
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertInBatchesAsync<T>(
        IReadOnlyList<T> records,
        string batchKind,
        NormalizedImportRequest request,
        Func<IReadOnlyList<T>, Task> insertBatchAsync,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return;
        }

        var batchNumber = 0;
        foreach (var batch in records.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            batchNumber++;

            _logger.LogInformation(
                "Batch insert started. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} batch_kind={BatchKind} batch_number={BatchNumber} record_count={RecordCount}",
                OperationName,
                request.CorrelationId,
                request.CaseId,
                request.SourceImportId,
                batchKind,
                batchNumber,
                batch.Length);

            await insertBatchAsync(batch).ConfigureAwait(false);

            _logger.LogInformation(
                "Batch insert completed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} batch_kind={BatchKind} batch_number={BatchNumber} record_count={RecordCount}",
                OperationName,
                request.CorrelationId,
                request.CaseId,
                request.SourceImportId,
                batchKind,
                batchNumber,
                batch.Length);
        }
    }

    private static async Task UpdateSourceImportCountsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceImportId,
        int recordCount,
        int warningCount,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE source_imports
            SET import_status = $importStatus,
                record_count = $recordCount,
                warning_count = $warningCount,
                updated_at_utc = $updatedAtUtc
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", sourceImportId);
        command.Parameters.AddWithValue("$importStatus", ImportStatusImported);
        command.Parameters.AddWithValue("$recordCount", recordCount);
        command.Parameters.AddWithValue("$warningCount", warningCount);
        command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(updatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ImportWarningRecord CreateImporterWarningRecord(
        NormalizedImportRequest request,
        ImportTabularDataResult readResult,
        PendingImporterWarning pendingWarning,
        IReadOnlyDictionary<string, string> artifactIdsByLocator)
    {
        var warning = pendingWarning.Warning;
        var artifactId = pendingWarning.ArtifactLocator is not null
            && artifactIdsByLocator.TryGetValue(pendingWarning.ArtifactLocator, out var linkedArtifactId)
                ? linkedArtifactId
                : null;

        var warningCode = warning.Code switch
        {
            ImportWarningCodes.FileNotFound => MessageImportWarningCodes.SourceFileNotFound,
            ImportWarningCodes.SelectedWorksheetNotFound => MessageImportWarningCodes.WorksheetNotFound,
            _ => MessageImportWarningCodes.RowParseWarning
        };

        var message = warning.Code switch
        {
            ImportWarningCodes.SelectedWorksheetNotFound => "The requested worksheet was not found in the workbook.",
            ImportWarningCodes.FileNotFound => "The selected source file could not be found during import.",
            _ => warning.Message
        };

        return CreateWarning(
            request.CaseId,
            request.SourceImportId,
            artifactId,
            warningCode,
            message,
            warning.ColumnName,
            rawValue: null);
    }

    private static ImportWarningRecord CreateWarning(
        string caseId,
        string sourceImportId,
        string? artifactId,
        string warningCode,
        string message,
        string? fieldName,
        string? rawValue)
    {
        return new ImportWarningRecord(
            Id: Guid.NewGuid().ToString("N"),
            CaseId: caseId,
            SourceImportId: sourceImportId,
            ArtifactId: artifactId,
            Severity: WarningSeverity,
            WarningCode: warningCode,
            Message: message,
            FieldName: NormalizeOptional(fieldName),
            RawValue: NormalizeNullableForStorage(rawValue),
            CreatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static string CreateAuditNewValueJson(
        NormalizedImportRequest request,
        int importedMessageCount,
        int sourceArtifactCount,
        int identityCountCreated,
        int identityCountReused,
        int recipientCount,
        int warningCount,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        var auditValue = new
        {
            case_id = request.CaseId,
            source_import_id = request.SourceImportId,
            source_kind = request.SourceKind.ToString().ToLowerInvariant(),
            worksheet_name = request.WorksheetName,
            imported_message_count = importedMessageCount,
            source_artifact_count = sourceArtifactCount,
            identity_count_created = identityCountCreated,
            identity_count_reused = identityCountReused,
            recipient_count = recipientCount,
            warning_count = warningCount,
            started_at_utc = FormatUtc(startedAtUtc),
            completed_at_utc = FormatUtc(completedAtUtc)
        };

        return JsonSerializer.Serialize(auditValue, JsonOptions);
    }

    private static string CreateSourceArtifactMetadataJson(
        ImportSourceKind sourceKind,
        string? worksheetName,
        int rowNumber,
        IReadOnlyList<ColumnValue> columnValues,
        IReadOnlyDictionary<string, string?> mappedValues)
    {
        var metadata = new
        {
            source_kind = sourceKind.ToString().ToLowerInvariant(),
            worksheet_name = worksheetName,
            row_number = rowNumber,
            column_values = columnValues.Select(static column => new
            {
                ordinal = column.Ordinal,
                name = column.Name,
                value = column.Value
            }),
            mapped_values = mappedValues
        };

        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    private static string CreateMessageMetadataJson(
        ImportSourceKind sourceKind,
        string? worksheetName,
        int rowNumber,
        string artifactLocator,
        IReadOnlyDictionary<string, string?> mappedValues,
        IdentityResolution? senderIdentity,
        int recipientCount,
        string timestampConfidence)
    {
        var metadata = new
        {
            source_kind = sourceKind.ToString().ToLowerInvariant(),
            worksheet_name = worksheetName,
            row_number = rowNumber,
            artifact_locator = artifactLocator,
            mapped_values = mappedValues,
            normalization = new
            {
                sender_identity_type = senderIdentity?.IdentityType,
                recipient_count = recipientCount,
                timestamp_confidence = timestampConfidence
            }
        };

        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    private static IReadOnlyList<ColumnValue> BuildColumnValues(
        IReadOnlyList<ImportPreviewColumn> columns,
        IReadOnlyList<string?> values)
    {
        var results = new List<ColumnValue>(columns.Count);
        foreach (var column in columns)
        {
            results.Add(new ColumnValue(
                column.Ordinal,
                column.SourceColumnName,
                column.Ordinal < values.Count ? values[column.Ordinal] : null));
        }

        return results;
    }

    private static IReadOnlyDictionary<string, string?> ResolveMappedValues(
        IReadOnlyList<MessageImportFieldMapping> fieldMappings,
        IReadOnlyList<ImportPreviewColumn> columns,
        IReadOnlyList<string?> values)
    {
        var results = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var mapping in fieldMappings)
        {
            results[mapping.DumpLensFieldName] = ResolveMappedValue(mapping, columns, values);
        }

        return results;
    }

    private static string? ResolveMappedValue(
        MessageImportFieldMapping mapping,
        IReadOnlyList<ImportPreviewColumn> columns,
        IReadOnlyList<string?> values)
    {
        if (mapping.SourceColumnOrdinal is int ordinal &&
            ordinal >= 0 &&
            ordinal < values.Count)
        {
            return values[ordinal];
        }

        if (!string.IsNullOrWhiteSpace(mapping.SourceColumnName))
        {
            var matchingColumn = columns.FirstOrDefault(column =>
                string.Equals(column.SourceColumnName, mapping.SourceColumnName, StringComparison.Ordinal));
            if (matchingColumn is not null && matchingColumn.Ordinal < values.Count)
            {
                return values[matchingColumn.Ordinal];
            }
        }

        return null;
    }

    private static string? ResolvePlatform(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalizedCandidate = NormalizeOptional(candidate);
            if (normalizedCandidate is not null)
            {
                return normalizedCandidate;
            }
        }

        return null;
    }

    private static string DetermineImportConfidence(
        string timestampConfidence,
        IdentityResolution? senderIdentity,
        IReadOnlyList<IdentityResolution> recipientResolutions,
        string? bodyValue)
    {
        var lowSignals =
            string.Equals(timestampConfidence, TimestampNormalizationConfidence.Low, StringComparison.Ordinal)
            || string.Equals(timestampConfidence, TimestampNormalizationConfidence.Unknown, StringComparison.Ordinal)
            || senderIdentity is null
            || recipientResolutions.Count == 0
            || string.IsNullOrWhiteSpace(bodyValue);

        if (lowSignals)
        {
            return "low";
        }

        var mediumSignals =
            string.Equals(timestampConfidence, TimestampNormalizationConfidence.Medium, StringComparison.Ordinal)
            || (senderIdentity is not null
                && string.Equals(senderIdentity.Confidence, IdentityNormalizationConfidence.Medium, StringComparison.Ordinal))
            || recipientResolutions.Any(static resolution =>
                string.Equals(resolution.Confidence, IdentityNormalizationConfidence.Medium, StringComparison.Ordinal)
                || string.Equals(resolution.Confidence, IdentityNormalizationConfidence.Low, StringComparison.Ordinal)
                || string.Equals(resolution.Confidence, IdentityNormalizationConfidence.Unknown, StringComparison.Ordinal));

        return mediumSignals ? "medium" : "high";
    }

    private static string BuildArtifactLocator(string? worksheetName, int? rowNumber)
    {
        var normalizedWorksheetName = NormalizeOptional(worksheetName);
        return normalizedWorksheetName is null
            ? rowNumber.HasValue
                ? string.Create(CultureInfo.InvariantCulture, $"row:{rowNumber.Value}")
                : "row:unknown"
            : rowNumber.HasValue
                ? string.Create(CultureInfo.InvariantCulture, $"worksheet:{normalizedWorksheetName};row:{rowNumber.Value}")
                : $"worksheet:{normalizedWorksheetName}";
    }

    private static string InferIdentityType(string rawValue, string? sourcePlatform, string? defaultPlatform)
    {
        if (LooksLikeEmail(rawValue))
        {
            return IdentityTypes.Email;
        }

        if (LooksLikePhoneNumber(rawValue))
        {
            return IdentityTypes.PhoneNumber;
        }

        if (LooksLikeSocialHandle(rawValue, sourcePlatform, defaultPlatform))
        {
            return IdentityTypes.SocialHandle;
        }

        return IdentityTypes.ContactName;
    }

    private static bool LooksLikeEmail(string value)
    {
        var trimmedValue = value.Trim();
        var atIndex = trimmedValue.IndexOf('@', StringComparison.Ordinal);
        return atIndex > 0
               && atIndex < trimmedValue.Length - 1
               && trimmedValue.IndexOf('.', atIndex) >= 0;
    }

    private static bool LooksLikePhoneNumber(string value)
    {
        var digits = value.Count(char.IsDigit);
        if (digits < 7)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsDigit(character) || char.IsWhiteSpace(character) || character is '+' or '(' or ')' or '-' or '.')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool LooksLikeSocialHandle(string value, string? sourcePlatform, string? defaultPlatform)
    {
        var trimmedValue = value.Trim();
        if (trimmedValue.StartsWith('@'))
        {
            return true;
        }

        if (trimmedValue.Contains("://", StringComparison.Ordinal))
        {
            return true;
        }

        if (trimmedValue.Contains(' '))
        {
            return false;
        }

        var platform = ResolvePlatform(sourcePlatform, defaultPlatform);
        return platform is not null
               && !LooksLikePhoneNumber(trimmedValue)
               && !LooksLikeEmail(trimmedValue)
               && (trimmedValue.Contains('_') || trimmedValue.Contains('.') || trimmedValue.Contains('-'));
    }

    private static string ClassifyIdentityWarningCode(string warningCode, bool isSender)
    {
        var ambiguousCode = isSender
            ? MessageImportWarningCodes.AmbiguousSenderIdentity
            : MessageImportWarningCodes.AmbiguousRecipientIdentity;
        var invalidCode = isSender
            ? MessageImportWarningCodes.InvalidSenderIdentity
            : MessageImportWarningCodes.InvalidRecipientIdentity;

        return warningCode switch
        {
            IdentityNormalizeWarningCodes.InvalidPhoneNumber => invalidCode,
            IdentityNormalizeWarningCodes.InvalidEmail => invalidCode,
            IdentityNormalizeWarningCodes.InvalidHandle => invalidCode,
            IdentityNormalizeWarningCodes.AmbiguousHandle => ambiguousCode,
            IdentityNormalizeWarningCodes.AmbiguousPhoneNumber => ambiguousCode,
            IdentityNormalizeWarningCodes.NormalizedValueEmpty => ambiguousCode,
            _ => ambiguousCode
        };
    }

    private static string CreateIdentityCacheKey(
        string identityType,
        IdentityNormalizeResult normalization,
        string rawValue)
    {
        var normalizedValue = NormalizeNullableForStorage(normalization.NormalizedValue);
        if (normalizedValue is not null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{identityType}|norm|{normalizedValue}");
        }

        var fallbackValue = NormalizeNullableForStorage(normalization.DisplayValue) ?? NormalizeOptional(rawValue) ?? string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{identityType}|raw|{fallbackValue}");
    }

    private static string? NormalizeStoredText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? NormalizeMessageBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var normalized = string.Join(
            " ",
            body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.ToLowerInvariant();
    }

    private static string? ComputeBodySha256(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string BuildConnectionString(string caseDatabasePath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = caseDatabasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    private static async Task EnableForeignKeysAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private ISourceImporter ResolveImporter(ImportSourceKind sourceKind, string sourceFilePath)
    {
        if (_importers.TryGetValue(sourceKind, out var importer) && importer.CanHandle(sourceFilePath))
        {
            return importer;
        }

        throw new InvalidOperationException("The requested source kind is not supported by the configured importers.");
    }

    private static NormalizedImportRequest ValidateAndNormalize(ImportMessagesRequest request)
    {
        var caseId = NormalizeRequired(request.CaseId, nameof(request.CaseId));
        var sourceImportId = NormalizeRequired(request.SourceImportId, nameof(request.SourceImportId));
        var caseDatabasePath = NormalizeAbsoluteFilePath(request.CaseDatabasePath, nameof(request.CaseDatabasePath));

        if (!File.Exists(caseDatabasePath) || Directory.Exists(caseDatabasePath))
        {
            throw new FileNotFoundException("The case database path must exist and point to a file.", caseDatabasePath);
        }

        if (request.FieldMappings is null)
        {
            throw new ArgumentNullException(nameof(request.FieldMappings));
        }

        var normalizedFieldMappings = request.FieldMappings
            .Select(static mapping => new MessageImportFieldMapping
            {
                DumpLensFieldName = NormalizeRequired(mapping.DumpLensFieldName, nameof(mapping.DumpLensFieldName)),
                SourceColumnName = NormalizeOptional(mapping.SourceColumnName),
                SourceColumnOrdinal = mapping.SourceColumnOrdinal
            })
            .ToArray();

        var packageRootPath = Path.GetDirectoryName(caseDatabasePath)
                              ?? throw new InvalidOperationException("The case database path must resolve to a package directory.");

        var sourceFilePath = NormalizeOptional(request.SourceFilePath) is string selectedSourceFilePath
            ? NormalizeAbsoluteFilePath(selectedSourceFilePath, nameof(request.SourceFilePath))
            : null;

        return new NormalizedImportRequest(
            CaseId: caseId,
            SourceImportId: sourceImportId,
            CaseDatabasePath: caseDatabasePath,
            PackageRootPath: packageRootPath,
            SourceFilePath: sourceFilePath,
            SourceKind: request.SourceKind,
            WorksheetName: NormalizeOptional(request.WorksheetName),
            FieldMappings: normalizedFieldMappings,
            TimezoneAssumption: NormalizeOptional(request.TimezoneAssumption),
            DefaultPlatform: NormalizeOptional(request.DefaultPlatform),
            ImportedByUserId: NormalizeOptional(request.ImportedByUserId),
            CorrelationId: NormalizeCorrelationId(request.CorrelationId),
            RowLimit: request.RowLimit);
    }

    private static string ResolveSourceFilePath(NormalizedImportRequest request, SourceImportLookup sourceImport)
    {
        if (request.SourceFilePath is not null)
        {
            return request.SourceFilePath;
        }

        var storedRelativePath = NormalizeOptional(sourceImport.StoredFilePath);
        if (storedRelativePath is null)
        {
            throw new InvalidOperationException("The registered source import does not contain a stored file path.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(request.PackageRootPath, storedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        return fullPath;
    }

    private static IReadOnlyList<string> GetRequiredMappingNames()
    {
        return
        [
            ImportFieldNames.Timestamp,
            ImportFieldNames.Sender,
            ImportFieldNames.Recipient,
            ImportFieldNames.MessageBody
        ];
    }

    private static bool TryGetFieldMapping(
        IReadOnlyList<MessageImportFieldMapping> mappings,
        string fieldName,
        out MessageImportFieldMapping? mapping)
    {
        mapping = mappings.FirstOrDefault(candidate =>
            string.Equals(candidate.DumpLensFieldName, fieldName, StringComparison.Ordinal));
        return mapping is not null;
    }

    private static string NormalizeCorrelationId(string? correlationId)
    {
        return NormalizeOptional(correlationId) ?? Guid.NewGuid().ToString("N");
    }

    private static string NormalizeAbsoluteFilePath(string path, string parameterName)
    {
        if (!Path.IsPathRooted(path))
        {
            throw new ArgumentException("The path must be absolute.", parameterName);
        }

        return Path.GetFullPath(path.Trim());
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string? NormalizeNullableForStorage(string? value)
    {
        var normalized = NormalizeOptional(value);
        return normalized is null || normalized.Length == 0
            ? null
            : normalized;
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static object ToSqlValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value;
    }

    private static string? TryGetFileExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetExtension(path.Trim());
        }
        catch
        {
            return null;
        }
    }

    private sealed record NormalizedImportRequest(
        string CaseId,
        string SourceImportId,
        string CaseDatabasePath,
        string PackageRootPath,
        string? SourceFilePath,
        ImportSourceKind SourceKind,
        string? WorksheetName,
        IReadOnlyList<MessageImportFieldMapping> FieldMappings,
        string? TimezoneAssumption,
        string? DefaultPlatform,
        string? ImportedByUserId,
        string CorrelationId,
        int? RowLimit);

    private sealed record SourceImportLookup(
        string Id,
        string CaseId,
        string SourceName,
        string SourceType,
        string? Platform,
        string? StoredFilePath,
        string ImportStatus);

    private sealed record SourceArtifactRecord(
        string Id,
        string CaseId,
        string SourceImportId,
        string ArtifactType,
        string ArtifactLocator,
        int RowNumber,
        string? ObjectPath,
        string? ProviderObjectId,
        string RawMetadataJson,
        DateTimeOffset CreatedAtUtc);

    private sealed record MessageRecord(
        string Id,
        string CaseId,
        string SourceImportId,
        string SourceArtifactId,
        string? Platform,
        string? SourceThreadId,
        string? ProviderMessageId,
        string? EventTimeOriginal,
        string? EventTimeUtc,
        string? Timezone,
        string? SenderIdentityId,
        string? Direction,
        string? MessageBody,
        string? MessageBodyNormalized,
        string? MessageBodySha256,
        bool HasAttachments,
        string ImportConfidence,
        string OriginalMetadataJson,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record MessageRecipientRecord(
        string Id,
        string CaseId,
        string MessageId,
        string RecipientIdentityId,
        string RecipientRole,
        DateTimeOffset CreatedAtUtc);

    private sealed record ImportWarningRecord(
        string Id,
        string CaseId,
        string SourceImportId,
        string? ArtifactId,
        string Severity,
        string WarningCode,
        string Message,
        string? FieldName,
        string? RawValue,
        DateTimeOffset CreatedAtUtc);

    private sealed record IdentityRecord(
        string Id,
        string CaseId,
        string IdentityType,
        string RawValue,
        string? NormalizedValue,
        string? DisplayValue,
        string? SourceImportId,
        string? Platform,
        string Confidence,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record IdentityResolution(
        string IdentityId,
        string IdentityType,
        string Confidence);

    private sealed record TimestampImportResolution(
        string? NormalizedUtc,
        string? ResolvedTimezone,
        string Confidence);

    private sealed record ColumnValue(
        int Ordinal,
        string Name,
        string? Value);

    private sealed record PendingImporterWarning(
        ImportWarning Warning,
        string? ArtifactLocator);

    private sealed class ImportState
    {
        public HashSet<string> CreatedIdentityIds { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, IdentityRecord> IdentityCache { get; } = new(StringComparer.Ordinal);

        public List<MessageRecord> Messages { get; } = [];

        public List<MessageRecipientRecord> MessageRecipients { get; } = [];

        public List<PendingImporterWarning> PendingImporterWarnings { get; } = [];

        public HashSet<string> ReusedIdentityIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> SeenProviderMessageIds { get; } = new(StringComparer.Ordinal);

        public List<SourceArtifactRecord> SourceArtifacts { get; } = [];

        public Dictionary<string, string> SourceArtifactsByLocator { get; } = new(StringComparer.Ordinal);

        public List<ImportWarningRecord> Warnings { get; } = [];
    }
}
