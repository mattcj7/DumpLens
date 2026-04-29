using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using DumpLens.Application.Audit;
using DumpLens.Application.Cases;
using DumpLens.Application.FileHashing;
using DumpLens.Application.Identities;
using DumpLens.Application.Imports;
using DumpLens.Application.MessageImports;
using DumpLens.Application.Sources;
using DumpLens.Application.Timestamps;
using DumpLens.Ingestion.Csv;
using DumpLens.Ingestion.Xlsx;
using DumpLens.Persistence.Audit;
using DumpLens.Persistence.Cases;
using DumpLens.Persistence.MessageImports;
using DumpLens.Persistence.Sources;
using DumpLens.Tests.Integration.CasePackages;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DumpLens.Tests.Integration.MessageImports;

public sealed class SqliteMessageImportServiceTests
{
    [Fact]
    public async Task ImportAsync_CsvSource_PersistsMessagesArtifactsRecipientsWarningsAndAudit()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        var csvFilePath = Path.Combine(tempDirectory.DirectoryPath, "synthetic-messages.csv");
        await File.WriteAllTextAsync(
            csvFilePath,
            string.Join(
                "\n",
                [
                    "timestamp,sender,recipient,message_body,platform,direction,thread_id,message_id,attachment",
                    "4/1/2026 8:00 AM,555-111-2222,555-333-4444,First synthetic body,sms,outgoing,thread-1,msg-001,",
                    "4/1/2026 8:05 AM,555-111-2222,555-333-4444;555-333-5555,Second synthetic body,sms,outgoing,thread-1,msg-002,photo.jpg",
                    "not-a-timestamp,555-111-2222,,Third synthetic body,sms,outgoing,thread-1,msg-003,"
                ]),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var registerResult = await RegisterSourceAsync(
            caseResult,
            csvFilePath,
            sourceName: "Synthetic CSV Messages",
            sourceType: "csv_messages",
            platform: "sms",
            correlationId: "message-import-csv-register");

        var service = CreateService();
        var importResult = await service.ImportAsync(new ImportMessagesRequest
        {
            CaseId = caseResult.CaseId,
            SourceImportId = registerResult.SourceImportId,
            CaseDatabasePath = caseResult.DatabasePath,
            SourceFilePath = registerResult.StoredFilePath,
            SourceKind = ImportSourceKind.Csv,
            FieldMappings = CreateFieldMappings(
                ("timestamp", ImportFieldNames.Timestamp),
                ("sender", ImportFieldNames.Sender),
                ("recipient", ImportFieldNames.Recipient),
                ("message_body", ImportFieldNames.MessageBody),
                ("platform", ImportFieldNames.Platform),
                ("direction", ImportFieldNames.Direction),
                ("thread_id", ImportFieldNames.ThreadId),
                ("message_id", ImportFieldNames.MessageId),
                ("attachment", ImportFieldNames.Attachment)),
            TimezoneAssumption = "Eastern Standard Time",
            CorrelationId = "message-import-csv"
        });

        Assert.Equal(caseResult.CaseId, importResult.CaseId);
        Assert.Equal(registerResult.SourceImportId, importResult.SourceImportId);
        Assert.Equal(3, importResult.ImportedMessageCount);
        Assert.Equal(3, importResult.SourceArtifactCount);
        Assert.Equal(3, importResult.RecipientCount);
        Assert.Equal(3, importResult.IdentityCountCreated);
        Assert.Equal(2, importResult.IdentityCountReused);
        Assert.True(importResult.WarningCount >= 4);
        Assert.False(string.IsNullOrWhiteSpace(importResult.AuditEventId));
        Assert.True(importResult.CompletedAtUtc >= importResult.StartedAtUtc);

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        Assert.Equal(3, await CountRowsAsync(connection, "source_artifacts", registerResult.SourceImportId, "source_import_id"));
        Assert.Equal(3, await CountRowsAsync(connection, "messages", registerResult.SourceImportId, "source_import_id"));
        Assert.Equal(3, await CountRowsAsync(connection, "message_recipients"));
        Assert.Equal(3, await CountRowsAsync(connection, "identities"));
        Assert.Equal(importResult.WarningCount, await CountRowsAsync(connection, "import_warnings", registerResult.SourceImportId, "source_import_id"));

        var messages = await LoadMessagesAsync(connection, registerResult.SourceImportId);
        Assert.Equal(3, messages.Count);

        var firstMessage = messages.Single(message => message.ProviderMessageId == "msg-001");
        var secondMessage = messages.Single(message => message.ProviderMessageId == "msg-002");
        var thirdMessage = messages.Single(message => message.ProviderMessageId == "msg-003");

        Assert.Equal(firstMessage.SenderIdentityId, secondMessage.SenderIdentityId);
        Assert.Equal(secondMessage.SenderIdentityId, thirdMessage.SenderIdentityId);
        Assert.Equal("4/1/2026 8:00 AM", firstMessage.EventTimeOriginal);
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero), firstMessage.EventTimeUtc);
        Assert.Equal("Eastern Standard Time", firstMessage.Timezone);
        Assert.Equal(ComputeSha256Hex("First synthetic body"), firstMessage.MessageBodySha256);
        Assert.Contains(@"""row_number"":2", firstMessage.OriginalMetadataJson, StringComparison.Ordinal);
        Assert.Contains(@"""artifact_locator"":""row:2""", firstMessage.OriginalMetadataJson, StringComparison.Ordinal);
        Assert.Null(thirdMessage.EventTimeUtc);

        var warningCodes = await LoadWarningCodesAsync(connection, registerResult.SourceImportId);
        Assert.Contains(MessageImportWarningCodes.InvalidTimestamp, warningCodes);
        Assert.Contains(MessageImportWarningCodes.MissingRecipient, warningCodes);
        Assert.Contains(MessageImportWarningCodes.MultipleRecipientsSplit, warningCodes);
        Assert.Contains(MessageImportWarningCodes.AttachmentNotPersisted, warningCodes);

        var sourceArtifacts = await LoadSourceArtifactsAsync(connection, registerResult.SourceImportId);
        Assert.Equal(["row:2", "row:3", "row:4"], sourceArtifacts.Select(static artifact => artifact.ArtifactLocator).ToArray());
        Assert.All(sourceArtifacts, artifact => Assert.Contains(@"""mapped_values"":", artifact.RawMetadataJson, StringComparison.Ordinal));

        var sourceImport = await LoadSourceImportAsync(connection, registerResult.SourceImportId);
        Assert.NotNull(sourceImport);
        Assert.Equal("imported", sourceImport!.ImportStatus);
        Assert.Equal(importResult.ImportedMessageCount, sourceImport.RecordCount);
        Assert.Equal(importResult.WarningCount, sourceImport.WarningCount);

        var auditEvent = await LoadAuditEventAsync(connection, importResult.AuditEventId!);
        Assert.NotNull(auditEvent);
        Assert.Equal(caseResult.CaseId, auditEvent!.CaseId);
        Assert.Equal("messages_imported", auditEvent.ActionType);
        Assert.Equal("source_import", auditEvent.EntityType);
        Assert.Equal(registerResult.SourceImportId, auditEvent.EntityId);
        Assert.Equal("Message import completed.", auditEvent.Summary);
        Assert.Contains(@"""imported_message_count"":3", auditEvent.NewValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain("First synthetic body", auditEvent.NewValueJson, StringComparison.Ordinal);

        var auditLogger = new SqliteAuditLogger(BuildConnectionString(caseResult.DatabasePath));
        var verification = await auditLogger.VerifyChainAsync(caseResult.CaseId, correlationId: "message-import-csv-verify");
        Assert.True(verification.IsValid);
        Assert.Equal(3, verification.CheckedEventCount);
        Assert.Equal(AuditChainFailureCodes.None, verification.FailureCode);
    }

    [Fact]
    public async Task ImportAsync_XlsxSource_ResolvesRegisteredStoredFileAndPreservesWorksheetLocator()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        var xlsxFilePath = Path.Combine(tempDirectory.DirectoryPath, "synthetic-messages.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Messages");
            worksheet.Cell(1, 1).Value = "timestamp";
            worksheet.Cell(1, 2).Value = "sender";
            worksheet.Cell(1, 3).Value = "recipient";
            worksheet.Cell(1, 4).Value = "message_body";
            worksheet.Cell(1, 5).Value = "direction";
            worksheet.Cell(1, 6).Value = "thread_id";
            worksheet.Cell(1, 7).Value = "message_id";
            worksheet.Cell(2, 1).Value = "2026-04-01T12:00:00Z";
            worksheet.Cell(2, 2).Value = "alpha@example.test";
            worksheet.Cell(2, 3).Value = "@recipient_one";
            worksheet.Cell(2, 4).Value = "Workbook row one";
            worksheet.Cell(2, 5).Value = "incoming";
            worksheet.Cell(2, 6).Value = "xlsx-thread";
            worksheet.Cell(2, 7).Value = "xlsx-001";
            worksheet.Cell(3, 1).Value = "2026-04-01T12:05:00Z";
            worksheet.Cell(3, 2).Value = "alpha@example.test";
            worksheet.Cell(3, 3).Value = "@recipient_two";
            worksheet.Cell(3, 4).Value = "Workbook row two";
            worksheet.Cell(3, 5).Value = "incoming";
            worksheet.Cell(3, 6).Value = "xlsx-thread";
            worksheet.Cell(3, 7).Value = "xlsx-002";
            workbook.Worksheets.Add("Calls");
            workbook.SaveAs(xlsxFilePath);
        }

        var registerResult = await RegisterSourceAsync(
            caseResult,
            xlsxFilePath,
            sourceName: "Synthetic XLSX Messages",
            sourceType: "xlsx_messages",
            platform: null,
            correlationId: "message-import-xlsx-register");

        var service = CreateService();
        var importResult = await service.ImportAsync(new ImportMessagesRequest
        {
            CaseId = caseResult.CaseId,
            SourceImportId = registerResult.SourceImportId,
            CaseDatabasePath = caseResult.DatabasePath,
            SourceFilePath = null,
            SourceKind = ImportSourceKind.Xlsx,
            WorksheetName = "Messages",
            FieldMappings = CreateFieldMappings(
                ("timestamp", ImportFieldNames.Timestamp),
                ("sender", ImportFieldNames.Sender),
                ("recipient", ImportFieldNames.Recipient),
                ("message_body", ImportFieldNames.MessageBody),
                ("direction", ImportFieldNames.Direction),
                ("thread_id", ImportFieldNames.ThreadId),
                ("message_id", ImportFieldNames.MessageId)),
            DefaultPlatform = "signal",
            CorrelationId = "message-import-xlsx"
        });

        Assert.Equal(2, importResult.ImportedMessageCount);
        Assert.Equal(2, importResult.SourceArtifactCount);
        Assert.Equal(3, importResult.IdentityCountCreated);
        Assert.Equal(1, importResult.IdentityCountReused);
        Assert.Equal(2, importResult.RecipientCount);
        Assert.Equal(0, importResult.WarningCount);

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        var sourceArtifacts = await LoadSourceArtifactsAsync(connection, registerResult.SourceImportId);
        Assert.Equal(
            ["worksheet:Messages;row:2", "worksheet:Messages;row:3"],
            sourceArtifacts.Select(static artifact => artifact.ArtifactLocator).ToArray());
        Assert.All(sourceArtifacts, artifact => Assert.Contains(@"""worksheet_name"":""Messages""", artifact.RawMetadataJson, StringComparison.Ordinal));

        var messages = await LoadMessagesAsync(connection, registerResult.SourceImportId);
        Assert.Equal(2, messages.Count);
        Assert.All(messages, message => Assert.Equal("signal", message.Platform));
        Assert.All(messages, message => Assert.Equal(new DateTimeOffset(DateTime.Parse(message.EventTimeOriginal!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).ToUniversalTime(), message.EventTimeUtc));

        var auditLogger = new SqliteAuditLogger(BuildConnectionString(caseResult.DatabasePath));
        var verification = await auditLogger.VerifyChainAsync(caseResult.CaseId, correlationId: "message-import-xlsx-verify");
        Assert.True(verification.IsValid);
    }

    [Fact]
    public async Task ImportAsync_EmitsEvidenceSafeStructuredLogsForSuccessAndFailure()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        const string sensitiveToken = "TOP_SECRET_MESSAGE_TOKEN";
        var csvFilePath = Path.Combine(tempDirectory.DirectoryPath, "log-sensitive.csv");
        await File.WriteAllTextAsync(
            csvFilePath,
            string.Join(
                "\n",
                [
                    "timestamp,sender,recipient,message_body",
                    $"4/1/2026 8:00 AM,{sensitiveToken},{sensitiveToken},{sensitiveToken}"
                ]),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var registerResult = await RegisterSourceAsync(
            caseResult,
            csvFilePath,
            sourceName: sensitiveToken,
            sourceType: "csv_messages",
            platform: null,
            correlationId: "message-import-log-register");

        var logger = new TestLogger<SqliteMessageImportService>();
        var service = CreateService(logger);

        await service.ImportAsync(new ImportMessagesRequest
        {
            CaseId = caseResult.CaseId,
            SourceImportId = registerResult.SourceImportId,
            CaseDatabasePath = caseResult.DatabasePath,
            SourceFilePath = registerResult.StoredFilePath,
            SourceKind = ImportSourceKind.Csv,
            FieldMappings = CreateFieldMappings(
                ("timestamp", ImportFieldNames.Timestamp),
                ("sender", ImportFieldNames.Sender),
                ("recipient", ImportFieldNames.Recipient),
                ("message_body", ImportFieldNames.MessageBody)),
            TimezoneAssumption = "Eastern Standard Time",
            CorrelationId = "message-import-log-success"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(new ImportMessagesRequest
        {
            CaseId = caseResult.CaseId,
            SourceImportId = "missing-import-id",
            CaseDatabasePath = caseResult.DatabasePath,
            SourceFilePath = Path.Combine(tempDirectory.DirectoryPath, $"{sensitiveToken}.csv"),
            SourceKind = ImportSourceKind.Csv,
            FieldMappings = CreateFieldMappings(
                ("timestamp", ImportFieldNames.Timestamp),
                ("sender", ImportFieldNames.Sender),
                ("recipient", ImportFieldNames.Recipient),
                ("message_body", ImportFieldNames.MessageBody)),
            TimezoneAssumption = "Eastern Standard Time",
            CorrelationId = "message-import-log-failure"
        }));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Message import started.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source validation completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Rows parsed/read.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Batch insert started.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Batch insert completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Identities created/reused.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Messages inserted.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Warnings inserted.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source imports counts updated.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Message import audit event written.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Message import completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Message import failed.", StringComparison.Ordinal));

        var flattenedLogs = string.Join(
            Environment.NewLine,
            logger.Entries.Select(entry =>
            {
                var fields = string.Join(";", entry.State.Select(pair => $"{pair.Key}={pair.Value}"));
                return $"{entry.Level}|{entry.Message}|{fields}";
            }));

        Assert.DoesNotContain(sensitiveToken, flattenedLogs, StringComparison.Ordinal);
        Assert.All(
            logger.Entries,
            entry =>
            {
                Assert.True(entry.State.ContainsKey("Operation"));
                Assert.True(entry.State.ContainsKey("CorrelationId"));
                Assert.True(entry.State.ContainsKey("CaseId"));
                Assert.True(entry.State.ContainsKey("SourceImportId"));
            });
    }

    private static IReadOnlyList<MessageImportFieldMapping> CreateFieldMappings(params (string SourceColumnName, string DumpLensFieldName)[] mappings)
    {
        return mappings
            .Select((mapping, ordinal) => new MessageImportFieldMapping
            {
                DumpLensFieldName = mapping.DumpLensFieldName,
                SourceColumnName = mapping.SourceColumnName,
                SourceColumnOrdinal = ordinal
            })
            .ToArray();
    }

    private static SqliteMessageImportService CreateService(TestLogger<SqliteMessageImportService>? logger = null)
    {
        return new SqliteMessageImportService(
            [new CsvSourceImporter(), new XlsxSourceImporter()],
            CreateIdentityNormalizer(),
            CreateTimestampNormalizer(),
            logger: logger);
    }

    private static IIdentityNormalizer CreateIdentityNormalizer()
    {
        var normalizationAssembly = LoadNormalizationAssembly();
        var type = normalizationAssembly.GetType("DumpLens.Normalization.Identities.IdentityNormalizer", throwOnError: true)!;
        return (IIdentityNormalizer)Activator.CreateInstance(type)!;
    }

    private static ITimestampNormalizer CreateTimestampNormalizer()
    {
        var normalizationAssembly = LoadNormalizationAssembly();
        var type = normalizationAssembly.GetType("DumpLens.Normalization.Timestamps.TimestampNormalizer", throwOnError: true)!;
        return (ITimestampNormalizer)Activator.CreateInstance(type)!;
    }

    private static Assembly LoadNormalizationAssembly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var assemblyPath = Path.Combine(
            repositoryRoot,
            "src",
            "DumpLens.Normalization",
            "bin",
            "Debug",
            "net9.0",
            "DumpLens.Normalization.dll");

        return Assembly.LoadFrom(assemblyPath);
    }

    private static string FindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "DumpLens.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new InvalidOperationException("Could not locate the DumpLens repository root.");
    }

    private static async Task<CreateCaseResult> CreateCaseAsync(string parentDirectoryPath)
    {
        var caseService = new SqliteCaseService();
        return await caseService.CreateAsync(new CreateCaseRequest
        {
            CaseNumber = "DL-MSG-001",
            Title = "Synthetic Message Import Case",
            ParentDirectoryPath = parentDirectoryPath,
            CorrelationId = "message-import-case-create"
        });
    }

    private static async Task<RegisterSourceResult> RegisterSourceAsync(
        CreateCaseResult caseResult,
        string sourceFilePath,
        string sourceName,
        string sourceType,
        string? platform,
        string correlationId)
    {
        var registrationService = new SqliteSourceRegistrationService(
            new DeterministicSha256FileHashService(),
            new SqliteSourceImportRepository());

        return await registrationService.RegisterAsync(new RegisterSourceRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SelectedSourceFilePath = sourceFilePath,
            SourceName = sourceName,
            SourceType = sourceType,
            Platform = platform,
            CorrelationId = correlationId
        });
    }

    private static string BuildConnectionString(string databasePath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    private static async Task<int> CountRowsAsync(
        SqliteConnection connection,
        string tableName,
        string? filterValue = null,
        string? filterColumn = null)
    {
        await using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(filterColumn))
        {
            command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        }
        else
        {
            command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE {filterColumn} = $filterValue;";
            command.Parameters.AddWithValue("$filterValue", filterValue);
        }

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<List<StoredMessage>> LoadMessagesAsync(SqliteConnection connection, string sourceImportId)
    {
        var results = new List<StoredMessage>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                provider_message_id,
                sender_identity_id,
                event_time_original,
                event_time_utc,
                timezone,
                platform,
                message_body_sha256,
                original_metadata_json
            FROM messages
            WHERE source_import_id = $sourceImportId
            ORDER BY provider_message_id ASC;
            """;
        command.Parameters.AddWithValue("$sourceImportId", sourceImportId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new StoredMessage(
                ProviderMessageId: reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                SenderIdentityId: reader.IsDBNull(1) ? null : reader.GetString(1),
                EventTimeOriginal: reader.IsDBNull(2) ? null : reader.GetString(2),
                EventTimeUtc: reader.IsDBNull(3)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
                Timezone: reader.IsDBNull(4) ? null : reader.GetString(4),
                Platform: reader.IsDBNull(5) ? null : reader.GetString(5),
                MessageBodySha256: reader.IsDBNull(6) ? null : reader.GetString(6),
                OriginalMetadataJson: reader.IsDBNull(7) ? string.Empty : reader.GetString(7)));
        }

        return results;
    }

    private static async Task<List<string>> LoadWarningCodesAsync(SqliteConnection connection, string sourceImportId)
    {
        var results = new List<string>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT warning_code
            FROM import_warnings
            WHERE source_import_id = $sourceImportId
            ORDER BY warning_code ASC;
            """;
        command.Parameters.AddWithValue("$sourceImportId", sourceImportId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    private static async Task<List<StoredSourceArtifact>> LoadSourceArtifactsAsync(SqliteConnection connection, string sourceImportId)
    {
        var results = new List<StoredSourceArtifact>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT artifact_locator, raw_metadata_json
            FROM source_artifacts
            WHERE source_import_id = $sourceImportId
            ORDER BY row_number ASC;
            """;
        command.Parameters.AddWithValue("$sourceImportId", sourceImportId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new StoredSourceArtifact(
                ArtifactLocator: reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                RawMetadataJson: reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
        }

        return results;
    }

    private static async Task<StoredSourceImport?> LoadSourceImportAsync(SqliteConnection connection, string sourceImportId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT import_status, record_count, warning_count
            FROM source_imports
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", sourceImportId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new StoredSourceImport(
            ImportStatus: reader.GetString(0),
            RecordCount: reader.GetInt32(1),
            WarningCount: reader.GetInt32(2));
    }

    private static async Task<StoredAuditEvent?> LoadAuditEventAsync(SqliteConnection connection, string auditEventId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                case_id,
                action_type,
                entity_type,
                entity_id,
                summary,
                new_value_json
            FROM audit_events
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", auditEventId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new StoredAuditEvent(
            CaseId: reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            ActionType: reader.GetString(1),
            EntityType: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            EntityId: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            Summary: reader.GetString(4),
            NewValueJson: reader.IsDBNull(5) ? string.Empty : reader.GetString(5));
    }

    private static string ComputeSha256Hex(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed record StoredMessage(
        string ProviderMessageId,
        string? SenderIdentityId,
        string? EventTimeOriginal,
        DateTimeOffset? EventTimeUtc,
        string? Timezone,
        string? Platform,
        string? MessageBodySha256,
        string OriginalMetadataJson);

    private sealed record StoredSourceArtifact(
        string ArtifactLocator,
        string RawMetadataJson);

    private sealed record StoredSourceImport(
        string ImportStatus,
        int RecordCount,
        int WarningCount);

    private sealed record StoredAuditEvent(
        string CaseId,
        string ActionType,
        string EntityType,
        string EntityId,
        string Summary,
        string NewValueJson);

    private sealed class DeterministicSha256FileHashService : IFileHashService
    {
        public async Task<FileHashResult> ComputeHashAsync(
            FileHashRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var filePath = Path.GetFullPath(request.FilePath);
            var startedAtUtc = DateTimeOffset.UtcNow;
            var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : request.CorrelationId.Trim();

            await using var stream = new FileStream(
                filePath,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });

            var fileSizeBytes = stream.Length;
            var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            var completedAtUtc = DateTimeOffset.UtcNow;

            return new FileHashResult
            {
                CorrelationId = correlationId,
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Algorithm = FileHashAlgorithm.Sha256,
                HexDigest = Convert.ToHexString(hashBytes).ToLowerInvariant(),
                FileSizeBytes = fileSizeBytes,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                Duration = completedAtUtc - startedAtUtc
            };
        }

        public async Task<string> WriteSha256FileAsync(
            FileHashResult result,
            string targetFolderPath,
            string outputFileName = "sha256.txt",
            CancellationToken cancellationToken = default)
        {
            var outputPath = Path.Combine(targetFolderPath, outputFileName);
            var content = string.Join(
                "\n",
                [
                    "algorithm: SHA-256",
                    $"file_name: {result.FileName}",
                    $"file_size_bytes: {result.FileSizeBytes.ToString(CultureInfo.InvariantCulture)}",
                    $"sha256: {result.HexDigest}",
                    string.Empty
                ]);

            await File.WriteAllTextAsync(
                    outputPath,
                    content,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);

            return outputPath;
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NoOpScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var structuredState = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (state is IEnumerable<KeyValuePair<string, object?>> keyValuePairs)
            {
                foreach (var keyValuePair in keyValuePairs)
                {
                    if (keyValuePair.Key == "{OriginalFormat}")
                    {
                        continue;
                    }

                    structuredState[keyValuePair.Key] = keyValuePair.Value;
                }
            }

            Entries.Add(new LogEntry(logLevel, formatter(state, exception), structuredState, exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> State,
        Exception? Exception);

    private sealed class NoOpScope : IDisposable
    {
        public static NoOpScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
