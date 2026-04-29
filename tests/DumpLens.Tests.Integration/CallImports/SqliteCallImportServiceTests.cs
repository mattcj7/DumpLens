using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using DumpLens.Application.Audit;
using DumpLens.Application.CallImports;
using DumpLens.Application.Cases;
using DumpLens.Application.FileHashing;
using DumpLens.Application.Identities;
using DumpLens.Application.Imports;
using DumpLens.Application.Sources;
using DumpLens.Application.Timestamps;
using DumpLens.Ingestion.Csv;
using DumpLens.Ingestion.Xlsx;
using DumpLens.Persistence.Audit;
using DumpLens.Persistence.CallImports;
using DumpLens.Persistence.Cases;
using DumpLens.Persistence.Sources;
using DumpLens.Tests.Integration.CasePackages;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DumpLens.Tests.Integration.CallImports;

public sealed class SqliteCallImportServiceTests
{
    [Fact]
    public async Task ImportAsync_CsvSource_PersistsCallsArtifactsWarningsAndAudit()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        var csvFilePath = Path.Combine(tempDirectory.DirectoryPath, "synthetic-calls.csv");
        await File.WriteAllTextAsync(
            csvFilePath,
            string.Join(
                "\n",
                [
                    "timestamp,caller,callee,direction,duration,call_type,platform_or_carrier",
                    "4/1/2026 8:00 AM,555-111-2222,555-333-4444,outgoing,45,voice,carrier-a",
                    "4/1/2026 8:05 AM,555-111-2222,555-333-5555,outgoing,01:30,voice,",
                    "not-a-timestamp,555-111-2222,,incoming,bad duration,video,",
                    "2026-04-01T12:10:00Z,555-111-2222,555-333-4444,incoming,1m 30s,video,"
                ]),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var registerResult = await RegisterSourceAsync(
            caseResult,
            csvFilePath,
            sourceName: "Synthetic CSV Calls",
            sourceType: "csv_calls",
            platform: null,
            correlationId: "call-import-csv-register");

        var service = CreateService();
        var importResult = await service.ImportAsync(new ImportCallsRequest
        {
            CaseId = caseResult.CaseId,
            SourceImportId = registerResult.SourceImportId,
            CaseDatabasePath = caseResult.DatabasePath,
            SourceFilePath = registerResult.StoredFilePath,
            SourceKind = ImportSourceKind.Csv,
            FieldMappings = CreateFieldMappings(
                ("timestamp", CallImportFieldNames.Timestamp),
                ("caller", CallImportFieldNames.Caller),
                ("callee", CallImportFieldNames.Callee),
                ("direction", CallImportFieldNames.Direction),
                ("duration", CallImportFieldNames.Duration),
                ("call_type", CallImportFieldNames.CallType),
                ("platform_or_carrier", CallImportFieldNames.PlatformOrCarrier)),
            TimezoneAssumption = "Eastern Standard Time",
            DefaultPlatformOrCarrier = "carrier-default",
            CorrelationId = "call-import-csv"
        });

        Assert.Equal(caseResult.CaseId, importResult.CaseId);
        Assert.Equal(registerResult.SourceImportId, importResult.SourceImportId);
        Assert.Equal(4, importResult.ImportedCallCount);
        Assert.Equal(4, importResult.SourceArtifactCount);
        Assert.Equal(3, importResult.IdentityCountCreated);
        Assert.Equal(2, importResult.IdentityCountReused);
        Assert.True(importResult.WarningCount >= 3);
        Assert.False(string.IsNullOrWhiteSpace(importResult.AuditEventId));
        Assert.True(importResult.CompletedAtUtc >= importResult.StartedAtUtc);

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        Assert.Equal(4, await CountRowsAsync(connection, "source_artifacts", registerResult.SourceImportId, "source_import_id"));
        Assert.Equal(4, await CountRowsAsync(connection, "calls", registerResult.SourceImportId, "source_import_id"));
        Assert.Equal(3, await CountRowsAsync(connection, "identities"));
        Assert.Equal(importResult.WarningCount, await CountRowsAsync(connection, "import_warnings", registerResult.SourceImportId, "source_import_id"));

        var calls = await LoadCallsAsync(connection, registerResult.SourceImportId);
        Assert.Equal(4, calls.Count);

        var firstCall = calls.Single(call => call.OriginalMetadataJson.Contains(@"""row_number"":2", StringComparison.Ordinal));
        var secondCall = calls.Single(call => call.OriginalMetadataJson.Contains(@"""row_number"":3", StringComparison.Ordinal));
        var thirdCall = calls.Single(call => call.OriginalMetadataJson.Contains(@"""row_number"":4", StringComparison.Ordinal));
        var fourthCall = calls.Single(call => call.OriginalMetadataJson.Contains(@"""row_number"":5", StringComparison.Ordinal));

        Assert.Equal(firstCall.CallerIdentityId, secondCall.CallerIdentityId);
        Assert.Equal(firstCall.CallerIdentityId, thirdCall.CallerIdentityId);
        Assert.Equal(firstCall.CallerIdentityId, fourthCall.CallerIdentityId);
        Assert.Equal(firstCall.CalleeIdentityId, fourthCall.CalleeIdentityId);

        Assert.Equal("4/1/2026 8:00 AM", firstCall.EventTimeOriginal);
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero), firstCall.EventTimeUtc);
        Assert.Equal("Eastern Standard Time", firstCall.Timezone);
        Assert.Equal(45, firstCall.DurationSeconds);
        Assert.Equal(90, secondCall.DurationSeconds);
        Assert.Null(thirdCall.EventTimeUtc);
        Assert.Null(thirdCall.DurationSeconds);
        Assert.Equal(90, fourthCall.DurationSeconds);
        Assert.Equal("carrier-a", firstCall.PlatformOrCarrier);
        Assert.Equal("carrier-default", secondCall.PlatformOrCarrier);
        Assert.Contains(@"""artifact_locator"":""row:2""", firstCall.OriginalMetadataJson, StringComparison.Ordinal);
        Assert.Contains(@"""duration_status"":""parsed""", firstCall.OriginalMetadataJson, StringComparison.Ordinal);
        Assert.Contains(@"""duration_status"":""invalid""", thirdCall.OriginalMetadataJson, StringComparison.Ordinal);

        var warningCodes = await LoadWarningCodesAsync(connection, registerResult.SourceImportId);
        Assert.Contains(CallImportWarningCodes.InvalidTimestamp, warningCodes);
        Assert.Contains(CallImportWarningCodes.MissingCallee, warningCodes);
        Assert.Contains(CallImportWarningCodes.InvalidDuration, warningCodes);

        var sourceArtifacts = await LoadSourceArtifactsAsync(connection, registerResult.SourceImportId);
        Assert.Equal(["row:2", "row:3", "row:4", "row:5"], sourceArtifacts.Select(static artifact => artifact.ArtifactLocator).ToArray());
        Assert.All(sourceArtifacts, artifact => Assert.Contains(@"""mapped_values"":", artifact.RawMetadataJson, StringComparison.Ordinal));

        var sourceImport = await LoadSourceImportAsync(connection, registerResult.SourceImportId);
        Assert.NotNull(sourceImport);
        Assert.Equal("imported", sourceImport!.ImportStatus);
        Assert.Equal(importResult.ImportedCallCount, sourceImport.RecordCount);
        Assert.Equal(importResult.WarningCount, sourceImport.WarningCount);

        var auditEvent = await LoadAuditEventAsync(connection, importResult.AuditEventId!);
        Assert.NotNull(auditEvent);
        Assert.Equal(caseResult.CaseId, auditEvent!.CaseId);
        Assert.Equal("calls_imported", auditEvent.ActionType);
        Assert.Equal("source_import", auditEvent.EntityType);
        Assert.Equal(registerResult.SourceImportId, auditEvent.EntityId);
        Assert.Equal("Call import completed.", auditEvent.Summary);
        Assert.Contains(@"""imported_call_count"":4", auditEvent.NewValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain("555-111-2222", auditEvent.NewValueJson, StringComparison.Ordinal);

        var auditLogger = new SqliteAuditLogger(BuildConnectionString(caseResult.DatabasePath));
        var verification = await auditLogger.VerifyChainAsync(caseResult.CaseId, correlationId: "call-import-csv-verify");
        Assert.True(verification.IsValid);
        Assert.Equal(3, verification.CheckedEventCount);
        Assert.Equal(AuditChainFailureCodes.None, verification.FailureCode);
    }

    [Fact]
    public async Task ImportAsync_XlsxSource_ResolvesRegisteredStoredFileAndAcceptsSenderRecipientAliases()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        var xlsxFilePath = Path.Combine(tempDirectory.DirectoryPath, "synthetic-calls.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Calls");
            worksheet.Cell(1, 1).Value = "timestamp";
            worksheet.Cell(1, 2).Value = "sender";
            worksheet.Cell(1, 3).Value = "recipient";
            worksheet.Cell(1, 4).Value = "direction";
            worksheet.Cell(1, 5).Value = "duration";
            worksheet.Cell(1, 6).Value = "call_type";
            worksheet.Cell(2, 1).Value = "2026-04-01T12:00:00Z";
            worksheet.Cell(2, 2).Value = "alpha@example.test";
            worksheet.Cell(2, 3).Value = "@recipient_one";
            worksheet.Cell(2, 4).Value = "incoming";
            worksheet.Cell(2, 5).Value = "01:02:03";
            worksheet.Cell(2, 6).Value = "voice";
            worksheet.Cell(3, 1).Value = "2026-04-01T12:05:00Z";
            worksheet.Cell(3, 2).Value = "alpha@example.test";
            worksheet.Cell(3, 3).Value = "@recipient_two";
            worksheet.Cell(3, 4).Value = "incoming";
            worksheet.Cell(3, 5).Value = "2 min";
            worksheet.Cell(3, 6).Value = "video";
            workbook.Worksheets.Add("Messages");
            workbook.SaveAs(xlsxFilePath);
        }

        var registerResult = await RegisterSourceAsync(
            caseResult,
            xlsxFilePath,
            sourceName: "Synthetic XLSX Calls",
            sourceType: "xlsx_calls",
            platform: "carrier-source",
            correlationId: "call-import-xlsx-register");

        var service = CreateService();
        var importResult = await service.ImportAsync(new ImportCallsRequest
        {
            CaseId = caseResult.CaseId,
            SourceImportId = registerResult.SourceImportId,
            CaseDatabasePath = caseResult.DatabasePath,
            SourceFilePath = null,
            SourceKind = ImportSourceKind.Xlsx,
            WorksheetName = "Calls",
            FieldMappings = CreateFieldMappings(
                ("timestamp", CallImportFieldNames.Timestamp),
                ("sender", CallImportFieldNames.SenderAlias),
                ("recipient", CallImportFieldNames.RecipientAlias),
                ("direction", CallImportFieldNames.Direction),
                ("duration", CallImportFieldNames.Duration),
                ("call_type", CallImportFieldNames.CallType)),
            CorrelationId = "call-import-xlsx"
        });

        Assert.Equal(2, importResult.ImportedCallCount);
        Assert.Equal(2, importResult.SourceArtifactCount);
        Assert.Equal(3, importResult.IdentityCountCreated);
        Assert.Equal(1, importResult.IdentityCountReused);
        Assert.Equal(0, importResult.WarningCount);

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        var sourceArtifacts = await LoadSourceArtifactsAsync(connection, registerResult.SourceImportId);
        Assert.Equal(
            ["worksheet:Calls;row:2", "worksheet:Calls;row:3"],
            sourceArtifacts.Select(static artifact => artifact.ArtifactLocator).ToArray());
        Assert.All(sourceArtifacts, artifact => Assert.Contains(@"""worksheet_name"":""Calls""", artifact.RawMetadataJson, StringComparison.Ordinal));

        var calls = await LoadCallsAsync(connection, registerResult.SourceImportId);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, call => Assert.Equal("carrier-source", call.PlatformOrCarrier));
        Assert.Equal(3723, calls.Single(call => call.OriginalMetadataJson.Contains(@"""row_number"":2", StringComparison.Ordinal)).DurationSeconds);
        Assert.Equal(120, calls.Single(call => call.OriginalMetadataJson.Contains(@"""row_number"":3", StringComparison.Ordinal)).DurationSeconds);
        Assert.All(
            calls,
            call => Assert.Equal(
                new DateTimeOffset(DateTime.Parse(call.EventTimeOriginal!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).ToUniversalTime(),
                call.EventTimeUtc));

        var auditLogger = new SqliteAuditLogger(BuildConnectionString(caseResult.DatabasePath));
        var verification = await auditLogger.VerifyChainAsync(caseResult.CaseId, correlationId: "call-import-xlsx-verify");
        Assert.True(verification.IsValid);
    }

    [Fact]
    public async Task ImportAsync_EmitsEvidenceSafeStructuredLogsForSuccessAndFailure()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        const string sensitiveToken = "TOP_SECRET_CALL_TOKEN";
        var csvFilePath = Path.Combine(tempDirectory.DirectoryPath, "log-sensitive-calls.csv");
        await File.WriteAllTextAsync(
            csvFilePath,
            string.Join(
                "\n",
                [
                    "timestamp,caller,callee,direction,duration",
                    $"4/1/2026 8:00 AM,{sensitiveToken},{sensitiveToken},incoming,{sensitiveToken}"
                ]),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var registerResult = await RegisterSourceAsync(
            caseResult,
            csvFilePath,
            sourceName: sensitiveToken,
            sourceType: "csv_calls",
            platform: null,
            correlationId: "call-import-log-register");

        var logger = new TestLogger<SqliteCallImportService>();
        var service = CreateService(logger);

        await service.ImportAsync(new ImportCallsRequest
        {
            CaseId = caseResult.CaseId,
            SourceImportId = registerResult.SourceImportId,
            CaseDatabasePath = caseResult.DatabasePath,
            SourceFilePath = registerResult.StoredFilePath,
            SourceKind = ImportSourceKind.Csv,
            FieldMappings = CreateFieldMappings(
                ("timestamp", CallImportFieldNames.Timestamp),
                ("caller", CallImportFieldNames.Caller),
                ("callee", CallImportFieldNames.Callee),
                ("direction", CallImportFieldNames.Direction),
                ("duration", CallImportFieldNames.Duration)),
            TimezoneAssumption = "Eastern Standard Time",
            CorrelationId = "call-import-log-success"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(new ImportCallsRequest
        {
            CaseId = caseResult.CaseId,
            SourceImportId = "missing-import-id",
            CaseDatabasePath = caseResult.DatabasePath,
            SourceFilePath = Path.Combine(tempDirectory.DirectoryPath, $"{sensitiveToken}.csv"),
            SourceKind = ImportSourceKind.Csv,
            FieldMappings = CreateFieldMappings(
                ("timestamp", CallImportFieldNames.Timestamp),
                ("caller", CallImportFieldNames.Caller),
                ("callee", CallImportFieldNames.Callee),
                ("direction", CallImportFieldNames.Direction),
                ("duration", CallImportFieldNames.Duration)),
            TimezoneAssumption = "Eastern Standard Time",
            CorrelationId = "call-import-log-failure"
        }));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Call import started.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source validation completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Rows parsed/read.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Batch insert started.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Batch insert completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Identities created/reused.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Calls inserted.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Warnings inserted.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source imports counts updated.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Call import audit event written.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Call import completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Call import failed.", StringComparison.Ordinal));

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

    private static IReadOnlyList<CallImportFieldMapping> CreateFieldMappings(params (string SourceColumnName, string DumpLensFieldName)[] mappings)
    {
        return mappings
            .Select((mapping, ordinal) => new CallImportFieldMapping
            {
                DumpLensFieldName = mapping.DumpLensFieldName,
                SourceColumnName = mapping.SourceColumnName,
                SourceColumnOrdinal = ordinal
            })
            .ToArray();
    }

    private static SqliteCallImportService CreateService(TestLogger<SqliteCallImportService>? logger = null)
    {
        return new SqliteCallImportService(
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
            CaseNumber = "DL-CALL-001",
            Title = "Synthetic Call Import Case",
            ParentDirectoryPath = parentDirectoryPath,
            CorrelationId = "call-import-case-create"
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

    private static async Task<List<StoredCall>> LoadCallsAsync(SqliteConnection connection, string sourceImportId)
    {
        var results = new List<StoredCall>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                caller_identity_id,
                callee_identity_id,
                event_time_original,
                event_time_utc,
                timezone,
                duration_seconds,
                platform_or_carrier,
                original_metadata_json
            FROM calls
            WHERE source_import_id = $sourceImportId
            ORDER BY created_at_utc ASC, id ASC;
            """;
        command.Parameters.AddWithValue("$sourceImportId", sourceImportId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new StoredCall(
                CallerIdentityId: reader.IsDBNull(0) ? null : reader.GetString(0),
                CalleeIdentityId: reader.IsDBNull(1) ? null : reader.GetString(1),
                EventTimeOriginal: reader.IsDBNull(2) ? null : reader.GetString(2),
                EventTimeUtc: reader.IsDBNull(3)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
                Timezone: reader.IsDBNull(4) ? null : reader.GetString(4),
                DurationSeconds: reader.IsDBNull(5) ? null : reader.GetInt32(5),
                PlatformOrCarrier: reader.IsDBNull(6) ? null : reader.GetString(6),
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

    private sealed record StoredCall(
        string? CallerIdentityId,
        string? CalleeIdentityId,
        string? EventTimeOriginal,
        DateTimeOffset? EventTimeUtc,
        string? Timezone,
        int? DurationSeconds,
        string? PlatformOrCarrier,
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
