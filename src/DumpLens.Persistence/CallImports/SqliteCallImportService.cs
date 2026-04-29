using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DumpLens.Application.Audit;
using DumpLens.Application.CallImports;
using DumpLens.Application.Identities;
using DumpLens.Application.Imports;
using DumpLens.Application.Timestamps;
using DumpLens.Persistence.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpLens.Persistence.CallImports;

public sealed partial class SqliteCallImportService : ICallImportService
{
    private const string AuditActionType = "calls_imported";
    private const string AuditEntityType = "source_import";
    private const int BatchSize = 200;
    private const string CallArtifactType = "call_row";
    private const string IdentityCreatedBy = "system";
    private const string ImportStatusImported = "imported";
    private const string OperationName = "call_import";
    private const string WarningSeverity = "warning";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Func<string, IAuditLogger> _auditLoggerFactory;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly IReadOnlyDictionary<ImportSourceKind, ISourceImporter> _importers;
    private readonly ILogger<SqliteCallImportService> _logger;
    private readonly ITimestampNormalizer _timestampNormalizer;

    public SqliteCallImportService(
        IEnumerable<ISourceImporter> importers,
        IIdentityNormalizer identityNormalizer,
        ITimestampNormalizer timestampNormalizer,
        Func<string, IAuditLogger>? auditLoggerFactory = null,
        ILogger<SqliteCallImportService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(importers);

        _importers = importers.ToDictionary(static importer => importer.SourceKind);
        _identityNormalizer = identityNormalizer ?? throw new ArgumentNullException(nameof(identityNormalizer));
        _timestampNormalizer = timestampNormalizer ?? throw new ArgumentNullException(nameof(timestampNormalizer));
        _auditLoggerFactory = auditLoggerFactory ?? (connectionString => new SqliteAuditLogger(connectionString));
        _logger = logger ?? NullLogger<SqliteCallImportService>.Instance;
    }

    public async Task<ImportCallsResult> ImportAsync(
        ImportCallsRequest request,
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
            "Call import started. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} source_kind={SourceKind} source_file_extension={SourceFileExtension} worksheet_name_present={WorksheetNamePresent} row_limit={RowLimit}",
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
                throw new InvalidOperationException("The requested source could not be read as supported tabular call data.");
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
            int importedCallCount;
            int sourceArtifactCount;
            int warningCount;
            int identityCountCreated;
            int identityCountReused;

            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                var importState = new ImportState();
                await StageGlobalWarningsAsync(importState, normalizedRequest, readResult, cancellationToken).ConfigureAwait(false);
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

                await InsertIdentitiesAsync(connection, transaction, importState.NewIdentities, normalizedRequest, cancellationToken)
                    .ConfigureAwait(false);

                await InsertCallsAsync(connection, transaction, importState.Calls, normalizedRequest, cancellationToken)
                    .ConfigureAwait(false);
                importedCallCount = importState.Calls.Count;

                _logger.LogInformation(
                    "Calls inserted. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} imported_call_count={ImportedCallCount}",
                    OperationName,
                    normalizedRequest.CorrelationId,
                    normalizedRequest.CaseId,
                    normalizedRequest.SourceImportId,
                    importedCallCount);

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
                        importedCallCount,
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
                    importedCallCount,
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
                        Summary = "Call import completed.",
                        NewValueJson = CreateAuditNewValueJson(
                            normalizedRequest,
                            importedCallCount,
                            sourceArtifactCount,
                            identityCountCreated,
                            identityCountReused,
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
                "Call import audit event written. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} audit_event_id={AuditEventId}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                auditEventId);

            _logger.LogInformation(
                "Call import completed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} imported_call_count={ImportedCallCount} source_artifact_count={SourceArtifactCount} warning_count={WarningCount}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                importedCallCount,
                sourceArtifactCount,
                warningCount);

            return new ImportCallsResult
            {
                CaseId = normalizedRequest.CaseId,
                SourceImportId = normalizedRequest.SourceImportId,
                ImportedCallCount = importedCallCount,
                SourceArtifactCount = sourceArtifactCount,
                IdentityCountCreated = identityCountCreated,
                IdentityCountReused = identityCountReused,
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
                "Call import failed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} failure_stage={FailureStage} failure_type={FailureType}",
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
                    CallImportWarningCodes.MissingRequiredMapping,
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
                ArtifactType: CallArtifactType,
                ArtifactLocator: artifactLocator,
                RowNumber: row.RowNumber,
                ObjectPath: readResult.SelectedWorksheetName,
                ProviderObjectId: null,
                RawMetadataJson: artifactMetadataJson,
                CreatedAtUtc: DateTimeOffset.UtcNow));

            try
            {
                var callerValue = NormalizeOptional(mappedValues.GetValueOrDefault(CallImportFieldNames.Caller));
                var calleeValue = NormalizeOptional(mappedValues.GetValueOrDefault(CallImportFieldNames.Callee));
                var timestampValue = mappedValues.GetValueOrDefault(CallImportFieldNames.Timestamp);
                var directionValue = NormalizeLowerInvariant(mappedValues.GetValueOrDefault(CallImportFieldNames.Direction));
                var callTypeValue = NormalizeLowerInvariant(mappedValues.GetValueOrDefault(CallImportFieldNames.CallType));
                var durationValue = mappedValues.GetValueOrDefault(CallImportFieldNames.Duration);
                var platformOrCarrierValue = ResolvePlatformOrCarrier(
                    mappedValues.GetValueOrDefault(CallImportFieldNames.PlatformOrCarrier),
                    request.DefaultPlatformOrCarrier,
                    sourceImport.Platform);

                if (callerValue is null)
                {
                    importState.Warnings.Add(CreateWarning(
                        request.CaseId,
                        request.SourceImportId,
                        artifactId,
                        CallImportWarningCodes.MissingCaller,
                        "The caller value is missing for this row.",
                        CallImportFieldNames.Caller,
                        mappedValues.GetValueOrDefault(CallImportFieldNames.Caller)));
                }

                if (calleeValue is null)
                {
                    importState.Warnings.Add(CreateWarning(
                        request.CaseId,
                        request.SourceImportId,
                        artifactId,
                        CallImportWarningCodes.MissingCallee,
                        "The callee value is missing for this row.",
                        CallImportFieldNames.Callee,
                        mappedValues.GetValueOrDefault(CallImportFieldNames.Callee)));
                }

                var callerIdentity = callerValue is null
                    ? null
                    : await ResolveIdentityAsync(
                            connection,
                            transaction,
                            request,
                            sourceImport,
                            callerValue,
                            artifactId,
                            CallImportFieldNames.Caller,
                            isCaller: true,
                            importState,
                            cancellationToken)
                        .ConfigureAwait(false);

                var calleeIdentity = calleeValue is null
                    ? null
                    : await ResolveIdentityAsync(
                            connection,
                            transaction,
                            request,
                            sourceImport,
                            calleeValue,
                            artifactId,
                            CallImportFieldNames.Callee,
                            isCaller: false,
                            importState,
                            cancellationToken)
                        .ConfigureAwait(false);

                var timestampResult = NormalizeTimestamp(request, timestampValue, artifactId, importState);
                var durationResult = NormalizeDuration(request, durationValue, artifactId, importState);

                if (platformOrCarrierValue is null)
                {
                    importState.Warnings.Add(CreateWarning(
                        request.CaseId,
                        request.SourceImportId,
                        artifactId,
                        CallImportWarningCodes.UnknownPlatformOrCarrier,
                        "No platform or carrier value was available from the field mapping, request default, or registered source metadata.",
                        CallImportFieldNames.PlatformOrCarrier,
                        mappedValues.GetValueOrDefault(CallImportFieldNames.PlatformOrCarrier)));
                }

                var createdAtUtc = DateTimeOffset.UtcNow;
                importState.Calls.Add(new CallRecord(
                    Id: Guid.NewGuid().ToString("N"),
                    CaseId: request.CaseId,
                    SourceImportId: request.SourceImportId,
                    SourceArtifactId: artifactId,
                    EventTimeOriginal: timestampValue,
                    EventTimeUtc: timestampResult.NormalizedUtc,
                    Timezone: timestampResult.ResolvedTimezone,
                    CallerIdentityId: callerIdentity?.IdentityId,
                    CalleeIdentityId: calleeIdentity?.IdentityId,
                    Direction: directionValue,
                    CallType: callTypeValue,
                    DurationSeconds: durationResult.DurationSeconds,
                    PlatformOrCarrier: platformOrCarrierValue,
                    ImportConfidence: DetermineImportConfidence(
                        timestampResult.Confidence,
                        callerIdentity,
                        calleeIdentity,
                        durationResult),
                    OriginalMetadataJson: CreateCallMetadataJson(
                        request.SourceKind,
                        readResult.SelectedWorksheetName,
                        row.RowNumber,
                        artifactLocator,
                        mappedValues,
                        callerIdentity,
                        calleeIdentity,
                        timestampResult.Confidence,
                        durationResult.Status),
                    CreatedAtUtc: createdAtUtc,
                    UpdatedAtUtc: createdAtUtc));
            }
            catch (Exception)
            {
                importState.Warnings.Add(CreateWarning(
                    request.CaseId,
                    request.SourceImportId,
                    artifactId,
                    CallImportWarningCodes.RowImportFailed,
                    "The row could not be imported as a call record.",
                    fieldName: null,
                    rawValue: null));
            }
        }

        foreach (var pendingWarning in importState.PendingImporterWarnings)
        {
            importState.Warnings.Add(CreateImporterWarningRecord(request, pendingWarning, importState.SourceArtifactsByLocator));
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
                CallImportWarningCodes.MissingTimestamp,
                "The timestamp value is missing for this row.",
                CallImportFieldNames.Timestamp,
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
                    ? CallImportWarningCodes.MissingTimestamp
                    : CallImportWarningCodes.InvalidTimestamp,
                warning.Message,
                CallImportFieldNames.Timestamp,
                timestampValue));
        }

        return new TimestampImportResolution(
            normalizedTimestamp.NormalizedUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            normalizedTimestamp.ResolvedTimezoneId ?? NormalizeOptional(request.TimezoneAssumption),
            normalizedTimestamp.Confidence);
    }

    private DurationParseResolution NormalizeDuration(
        NormalizedImportRequest request,
        string? durationValue,
        string artifactId,
        ImportState importState)
    {
        var durationMappingPresent = TryGetFieldMapping(request.FieldMappings, CallImportFieldNames.Duration, out _);
        var trimmedDuration = NormalizeOptional(durationValue);
        if (trimmedDuration is null)
        {
            if (durationMappingPresent)
            {
                importState.Warnings.Add(CreateWarning(
                    request.CaseId,
                    request.SourceImportId,
                    artifactId,
                    CallImportWarningCodes.MissingDuration,
                    "The duration value is missing for this row.",
                    CallImportFieldNames.Duration,
                    durationValue));
            }

            return new DurationParseResolution(null, durationMappingPresent ? "missing" : "not_mapped");
        }

        if (TryParseDurationSeconds(trimmedDuration, out var durationSeconds))
        {
            return new DurationParseResolution(durationSeconds, "parsed");
        }

        importState.Warnings.Add(CreateWarning(
            request.CaseId,
            request.SourceImportId,
            artifactId,
            CallImportWarningCodes.InvalidDuration,
            "The duration value could not be parsed into duration seconds.",
            CallImportFieldNames.Duration,
            durationValue));

        return new DurationParseResolution(null, "invalid");
    }

    private async Task<IdentityResolution?> ResolveIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedImportRequest request,
        SourceImportLookup sourceImport,
        string rawValue,
        string artifactId,
        string fieldName,
        bool isCaller,
        ImportState importState,
        CancellationToken cancellationToken)
    {
        var identityType = InferIdentityType(rawValue, sourceImport.Platform, request.DefaultPlatformOrCarrier);
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
                ClassifyIdentityWarningCode(warning.Code, isCaller),
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
            SourceArtifactId: artifactId,
            Platform: ResolvePlatformOrCarrier(sourceImport.Platform, request.DefaultPlatformOrCarrier),
            Confidence: normalization.Confidence,
            CreatedAtUtc: nowUtc,
            UpdatedAtUtc: nowUtc);

        importState.IdentityCache[cacheKey] = newIdentity;
        importState.NewIdentities.Add(newIdentity);
        importState.CreatedIdentityIds.Add(newIdentity.Id);

        return new IdentityResolution(newIdentity.Id, identityType, normalization.Confidence);
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
                    source_artifact_id,
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
                source_artifact_id,
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
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetString(9),
            DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
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

    private async Task InsertIdentitiesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<IdentityRecord> records,
        NormalizedImportRequest request,
        CancellationToken cancellationToken)
    {
        await InsertInBatchesAsync(
            records,
            "identities",
            request,
            async batch =>
            {
                foreach (var record in batch)
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
                            $sourceArtifactId,
                            $platform,
                            $confidence,
                            'unreviewed',
                            $createdBy,
                            NULL,
                            $createdAtUtc,
                            $updatedAtUtc
                        );
                        """;
                    command.Parameters.AddWithValue("$id", record.Id);
                    command.Parameters.AddWithValue("$caseId", record.CaseId);
                    command.Parameters.AddWithValue("$identityType", record.IdentityType);
                    command.Parameters.AddWithValue("$rawValue", record.RawValue);
                    command.Parameters.AddWithValue("$normalizedValue", ToSqlValue(record.NormalizedValue));
                    command.Parameters.AddWithValue("$displayValue", ToSqlValue(record.DisplayValue));
                    command.Parameters.AddWithValue("$sourceImportId", ToSqlValue(record.SourceImportId));
                    command.Parameters.AddWithValue("$sourceArtifactId", ToSqlValue(record.SourceArtifactId));
                    command.Parameters.AddWithValue("$platform", ToSqlValue(record.Platform));
                    command.Parameters.AddWithValue("$confidence", record.Confidence);
                    command.Parameters.AddWithValue("$createdBy", IdentityCreatedBy);
                    command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(record.CreatedAtUtc));
                    command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(record.UpdatedAtUtc));
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertCallsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<CallRecord> records,
        NormalizedImportRequest request,
        CancellationToken cancellationToken)
    {
        await InsertInBatchesAsync(
            records,
            "calls",
            request,
            async batch =>
            {
                foreach (var record in batch)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText =
                        """
                        INSERT INTO calls (
                            id,
                            case_id,
                            source_import_id,
                            source_artifact_id,
                            event_time_original,
                            event_time_utc,
                            timezone,
                            caller_identity_id,
                            callee_identity_id,
                            direction,
                            call_type,
                            duration_seconds,
                            platform_or_carrier,
                            cell_site_json,
                            import_confidence,
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
                            $eventTimeOriginal,
                            $eventTimeUtc,
                            $timezone,
                            $callerIdentityId,
                            $calleeIdentityId,
                            $direction,
                            $callType,
                            $durationSeconds,
                            $platformOrCarrier,
                            NULL,
                            $importConfidence,
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
                    command.Parameters.AddWithValue("$eventTimeOriginal", ToSqlValue(record.EventTimeOriginal));
                    command.Parameters.AddWithValue("$eventTimeUtc", ToSqlValue(record.EventTimeUtc));
                    command.Parameters.AddWithValue("$timezone", ToSqlValue(record.Timezone));
                    command.Parameters.AddWithValue("$callerIdentityId", ToSqlValue(record.CallerIdentityId));
                    command.Parameters.AddWithValue("$calleeIdentityId", ToSqlValue(record.CalleeIdentityId));
                    command.Parameters.AddWithValue("$direction", ToSqlValue(record.Direction));
                    command.Parameters.AddWithValue("$callType", ToSqlValue(record.CallType));
                    command.Parameters.AddWithValue("$durationSeconds", record.DurationSeconds.HasValue ? record.DurationSeconds.Value : DBNull.Value);
                    command.Parameters.AddWithValue("$platformOrCarrier", ToSqlValue(record.PlatformOrCarrier));
                    command.Parameters.AddWithValue("$importConfidence", record.ImportConfidence);
                    command.Parameters.AddWithValue("$originalMetadataJson", record.OriginalMetadataJson);
                    command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(record.CreatedAtUtc));
                    command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(record.UpdatedAtUtc));
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
            ImportWarningCodes.FileNotFound => CallImportWarningCodes.SourceFileNotFound,
            ImportWarningCodes.SelectedWorksheetNotFound => CallImportWarningCodes.WorksheetNotFound,
            _ => CallImportWarningCodes.RowParseWarning
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
        int importedCallCount,
        int sourceArtifactCount,
        int identityCountCreated,
        int identityCountReused,
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
            imported_call_count = importedCallCount,
            source_artifact_count = sourceArtifactCount,
            identity_count_created = identityCountCreated,
            identity_count_reused = identityCountReused,
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

    private static string CreateCallMetadataJson(
        ImportSourceKind sourceKind,
        string? worksheetName,
        int rowNumber,
        string artifactLocator,
        IReadOnlyDictionary<string, string?> mappedValues,
        IdentityResolution? callerIdentity,
        IdentityResolution? calleeIdentity,
        string timestampConfidence,
        string durationStatus)
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
                caller_identity_type = callerIdentity?.IdentityType,
                callee_identity_type = calleeIdentity?.IdentityType,
                timestamp_confidence = timestampConfidence,
                duration_status = durationStatus
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
        IReadOnlyList<CallImportFieldMapping> fieldMappings,
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
        CallImportFieldMapping mapping,
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

    private static string? ResolvePlatformOrCarrier(params string?[] candidates)
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
        IdentityResolution? callerIdentity,
        IdentityResolution? calleeIdentity,
        DurationParseResolution durationResolution)
    {
        var lowSignals =
            string.Equals(timestampConfidence, TimestampNormalizationConfidence.Low, StringComparison.Ordinal)
            || string.Equals(timestampConfidence, TimestampNormalizationConfidence.Unknown, StringComparison.Ordinal)
            || callerIdentity is null
            || calleeIdentity is null;

        if (lowSignals)
        {
            return "low";
        }

        var resolvedCallerIdentity = callerIdentity!;
        var resolvedCalleeIdentity = calleeIdentity!;
        var mediumSignals =
            string.Equals(timestampConfidence, TimestampNormalizationConfidence.Medium, StringComparison.Ordinal)
            || string.Equals(durationResolution.Status, "missing", StringComparison.Ordinal)
            || string.Equals(durationResolution.Status, "invalid", StringComparison.Ordinal)
            || string.Equals(resolvedCallerIdentity.Confidence, IdentityNormalizationConfidence.Medium, StringComparison.Ordinal)
            || string.Equals(resolvedCallerIdentity.Confidence, IdentityNormalizationConfidence.Low, StringComparison.Ordinal)
            || string.Equals(resolvedCallerIdentity.Confidence, IdentityNormalizationConfidence.Unknown, StringComparison.Ordinal)
            || string.Equals(resolvedCalleeIdentity.Confidence, IdentityNormalizationConfidence.Medium, StringComparison.Ordinal)
            || string.Equals(resolvedCalleeIdentity.Confidence, IdentityNormalizationConfidence.Low, StringComparison.Ordinal)
            || string.Equals(resolvedCalleeIdentity.Confidence, IdentityNormalizationConfidence.Unknown, StringComparison.Ordinal);

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

    private static string InferIdentityType(string rawValue, string? sourcePlatform, string? defaultPlatformOrCarrier)
    {
        if (LooksLikeEmail(rawValue))
        {
            return IdentityTypes.Email;
        }

        if (LooksLikePhoneNumber(rawValue))
        {
            return IdentityTypes.PhoneNumber;
        }

        if (LooksLikeSocialHandle(rawValue, sourcePlatform, defaultPlatformOrCarrier))
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

    private static bool LooksLikeSocialHandle(string value, string? sourcePlatform, string? defaultPlatformOrCarrier)
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

        var platform = ResolvePlatformOrCarrier(sourcePlatform, defaultPlatformOrCarrier);
        return platform is not null
               && !LooksLikePhoneNumber(trimmedValue)
               && !LooksLikeEmail(trimmedValue)
               && (trimmedValue.Contains('_') || trimmedValue.Contains('.') || trimmedValue.Contains('-'));
    }

    private static string ClassifyIdentityWarningCode(string warningCode, bool isCaller)
    {
        var ambiguousCode = isCaller
            ? CallImportWarningCodes.AmbiguousCallerIdentity
            : CallImportWarningCodes.AmbiguousCalleeIdentity;
        var invalidCode = isCaller
            ? CallImportWarningCodes.InvalidCallerIdentity
            : CallImportWarningCodes.InvalidCalleeIdentity;

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

    private static bool TryParseDurationSeconds(string value, out int durationSeconds)
    {
        durationSeconds = default;

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var plainSeconds) && plainSeconds >= 0)
        {
            durationSeconds = plainSeconds;
            return true;
        }

        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is 2 or 3 &&
            parts.All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            var numericParts = parts
                .Select(static part => int.Parse(part, CultureInfo.InvariantCulture))
                .ToArray();

            if (numericParts.Any(static part => part < 0))
            {
                return false;
            }

            durationSeconds = numericParts.Length == 2
                ? (numericParts[0] * 60) + numericParts[1]
                : (numericParts[0] * 3600) + (numericParts[1] * 60) + numericParts[2];
            return true;
        }

        var matches = DurationTokenRegex().Matches(value);
        if (matches.Count == 0)
        {
            return false;
        }

        var consumedLength = 0;
        var totalSeconds = 0;
        foreach (Match match in matches)
        {
            consumedLength += match.Length;
            var amount = int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            var unit = match.Groups["unit"].Value.ToLowerInvariant();

            totalSeconds += unit switch
            {
                "h" or "hr" or "hrs" or "hour" or "hours" => amount * 3600,
                "m" or "min" or "mins" or "minute" or "minutes" => amount * 60,
                "s" or "sec" or "secs" or "second" or "seconds" => amount,
                _ => 0
            };
        }

        var normalizedValue = DurationTokenRegex().Replace(value, string.Empty);
        if (!string.IsNullOrWhiteSpace(normalizedValue))
        {
            return false;
        }

        durationSeconds = totalSeconds;
        return true;
    }

    private static string? NormalizeLowerInvariant(string? value)
    {
        var normalized = NormalizeOptional(value);
        return normalized?.ToLowerInvariant();
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

    private static NormalizedImportRequest ValidateAndNormalize(ImportCallsRequest request)
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
            .Select(static mapping => new CallImportFieldMapping
            {
                DumpLensFieldName = CanonicalizeFieldName(mapping.DumpLensFieldName),
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
            DefaultPlatformOrCarrier: NormalizeOptional(request.DefaultPlatformOrCarrier),
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

        return Path.GetFullPath(Path.Combine(request.PackageRootPath, storedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static IReadOnlyList<string> GetRequiredMappingNames()
    {
        return
        [
            CallImportFieldNames.Timestamp,
            CallImportFieldNames.Caller,
            CallImportFieldNames.Callee,
            CallImportFieldNames.Direction
        ];
    }

    private static bool TryGetFieldMapping(
        IReadOnlyList<CallImportFieldMapping> mappings,
        string fieldName,
        out CallImportFieldMapping? mapping)
    {
        mapping = mappings.FirstOrDefault(candidate =>
            string.Equals(candidate.DumpLensFieldName, fieldName, StringComparison.Ordinal));
        return mapping is not null;
    }

    private static string CanonicalizeFieldName(string fieldName)
    {
        var normalized = NormalizeRequired(fieldName, nameof(fieldName)).ToLowerInvariant();
        return normalized switch
        {
            CallImportFieldNames.SenderAlias => CallImportFieldNames.Caller,
            CallImportFieldNames.RecipientAlias => CallImportFieldNames.Callee,
            CallImportFieldNames.PlatformAlias => CallImportFieldNames.PlatformOrCarrier,
            _ => normalized
        };
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

    [GeneratedRegex(@"(?<value>\d+)\s*(?<unit>h|hr|hrs|hour|hours|m|min|mins|minute|minutes|s|sec|secs|second|seconds)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationTokenRegex();

    private sealed record NormalizedImportRequest(
        string CaseId,
        string SourceImportId,
        string CaseDatabasePath,
        string PackageRootPath,
        string? SourceFilePath,
        ImportSourceKind SourceKind,
        string? WorksheetName,
        IReadOnlyList<CallImportFieldMapping> FieldMappings,
        string? TimezoneAssumption,
        string? DefaultPlatformOrCarrier,
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

    private sealed record CallRecord(
        string Id,
        string CaseId,
        string SourceImportId,
        string SourceArtifactId,
        string? EventTimeOriginal,
        string? EventTimeUtc,
        string? Timezone,
        string? CallerIdentityId,
        string? CalleeIdentityId,
        string? Direction,
        string? CallType,
        int? DurationSeconds,
        string? PlatformOrCarrier,
        string ImportConfidence,
        string OriginalMetadataJson,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

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
        string? SourceArtifactId,
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

    private sealed record DurationParseResolution(
        int? DurationSeconds,
        string Status);

    private sealed record ColumnValue(
        int Ordinal,
        string Name,
        string? Value);

    private sealed record PendingImporterWarning(
        ImportWarning Warning,
        string? ArtifactLocator);

    private sealed class ImportState
    {
        public List<CallRecord> Calls { get; } = [];

        public HashSet<string> CreatedIdentityIds { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, IdentityRecord> IdentityCache { get; } = new(StringComparer.Ordinal);

        public List<IdentityRecord> NewIdentities { get; } = [];

        public List<PendingImporterWarning> PendingImporterWarnings { get; } = [];

        public HashSet<string> ReusedIdentityIds { get; } = new(StringComparer.Ordinal);

        public List<SourceArtifactRecord> SourceArtifacts { get; } = [];

        public Dictionary<string, string> SourceArtifactsByLocator { get; } = new(StringComparer.Ordinal);

        public List<ImportWarningRecord> Warnings { get; } = [];
    }
}
