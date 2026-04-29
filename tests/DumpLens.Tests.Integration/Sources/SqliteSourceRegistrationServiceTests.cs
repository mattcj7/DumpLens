using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DumpLens.Application.Audit;
using DumpLens.Application.Cases;
using DumpLens.Application.FileHashing;
using DumpLens.Application.Sources;
using DumpLens.Persistence.Audit;
using DumpLens.Persistence.Cases;
using DumpLens.Persistence.Sources;
using DumpLens.Tests.Integration.CasePackages;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DumpLens.Tests.Integration.Sources;

public sealed class SqliteSourceRegistrationServiceTests
{
    [Fact]
    public async Task RegisterAsync_CopiesHashesWritesManifestPersistsRowAndAuditsRegistration()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        var sourceFilePath = Path.Combine(tempDirectory.DirectoryPath, "synthetic-source.csv");
        const string sourceContent = "timestamp,sender,recipient,message_body\n2026-04-01T12:00:00Z,alpha,beta,synthetic row\n";
        await File.WriteAllTextAsync(sourceFilePath, sourceContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var expectedSha256 = ComputeSha256Hex(sourceContent);

        var service = CreateService();
        var result = await service.RegisterAsync(new RegisterSourceRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SelectedSourceFilePath = sourceFilePath,
            SourceName = "Synthetic CSV Source",
            SourceType = "csv_messages",
            Platform = "sms",
            ImportedByUserId = null,
            Notes = "Synthetic notes for registration coverage.",
            SourceMetadataJson = "{ \"worksheet\": null, \"preview_row_count\": 10 }",
            CorrelationId = "source-register-e2e"
        });

        Assert.True(Guid.TryParseExact(result.SourceImportId, "N", out _));
        Assert.Equal(caseResult.CaseId, result.CaseId);
        Assert.Equal("Synthetic CSV Source", result.SourceName);
        Assert.Equal("csv_messages", result.SourceType);
        Assert.Equal("sms", result.Platform);
        Assert.Equal("synthetic-source.csv", result.OriginalFilename);
        Assert.Equal(expectedSha256, result.FileSha256);
        Assert.Equal(sourceContent.Length, result.FileSizeBytes);
        Assert.Equal("source-register-e2e", result.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(result.AuditEventId));
        Assert.True(Directory.Exists(result.SourceFolderPath));
        Assert.True(File.Exists(result.StoredFilePath));
        Assert.True(File.Exists(result.ManifestPath));
        Assert.True(File.Exists(result.Sha256FilePath));

        var copiedContent = await File.ReadAllTextAsync(result.StoredFilePath);
        Assert.Equal(sourceContent, copiedContent);

        var expectedStoredRelativePath = ToRelativePath(caseResult.PackageRootPath, result.StoredFilePath);
        var expectedSourceFolderRelativePath = ToRelativePath(caseResult.PackageRootPath, result.SourceFolderPath);
        var expectedSha256RelativePath = ToRelativePath(caseResult.PackageRootPath, result.Sha256FilePath);

        var sha256Text = await File.ReadAllTextAsync(result.Sha256FilePath);
        Assert.Equal(
            string.Join(
                "\n",
                [
                    "algorithm: SHA-256",
                    $"file_name: {Path.GetFileName(result.StoredFilePath)}",
                    $"file_size_bytes: {result.FileSizeBytes.ToString(CultureInfo.InvariantCulture)}",
                    $"sha256: {result.FileSha256}",
                    string.Empty
                ]),
            sha256Text);

        await using (var manifestStream = File.OpenRead(result.ManifestPath))
        {
            var manifest = await JsonSerializer.DeserializeAsync<SourceImportManifest>(manifestStream);

            Assert.NotNull(manifest);
            Assert.Equal("1", manifest.ManifestVersion);
            Assert.Equal(result.SourceImportId, manifest.SourceImportId);
            Assert.Equal(caseResult.CaseId, manifest.CaseId);
            Assert.Equal("Synthetic CSV Source", manifest.SourceName);
            Assert.Equal("csv_messages", manifest.SourceType);
            Assert.Equal("sms", manifest.Platform);
            Assert.Equal("synthetic-source.csv", manifest.OriginalFilename);
            Assert.Equal(expectedStoredRelativePath, manifest.StoredRelativePath);
            Assert.Equal(result.FileSizeBytes, manifest.FileSizeBytes);
            Assert.Equal(expectedSha256, manifest.FileSha256);
            Assert.Equal(result.ImportedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), manifest.ImportedAtUtc);
            Assert.Equal(expectedSourceFolderRelativePath, manifest.SourceFolderRelativePath);
            Assert.Equal(expectedSha256RelativePath, manifest.Sha256RelativePath);
            Assert.Equal("DumpLens", manifest.AppName);
            Assert.Equal("copy", manifest.CopyMode);
        }

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        var storedSourceImport = await LoadSourceImportAsync(connection, result.SourceImportId);
        Assert.NotNull(storedSourceImport);
        Assert.Equal(caseResult.CaseId, storedSourceImport.CaseId);
        Assert.Equal("Synthetic CSV Source", storedSourceImport.SourceName);
        Assert.Equal("csv_messages", storedSourceImport.SourceType);
        Assert.Equal("sms", storedSourceImport.Platform);
        Assert.Equal("synthetic-source.csv", storedSourceImport.OriginalFilename);
        Assert.Equal(sourceFilePath, storedSourceImport.OriginalFilePath);
        Assert.Equal(expectedStoredRelativePath, storedSourceImport.StoredFilePath);
        Assert.Equal(result.FileSizeBytes, storedSourceImport.FileSizeBytes);
        Assert.Equal(expectedSha256, storedSourceImport.FileSha256);
        Assert.Equal("registered", storedSourceImport.ImportStatus);
        Assert.Equal(0, storedSourceImport.RecordCount);
        Assert.Equal(0, storedSourceImport.WarningCount);
        Assert.Equal("Synthetic notes for registration coverage.", storedSourceImport.Notes);
        Assert.Equal("""{"worksheet":null,"preview_row_count":10}""", storedSourceImport.SourceMetadataJson);

        Assert.Equal(0, await CountRowsAsync(connection, "source_artifacts", result.SourceImportId, "source_import_id"));

        var auditEvent = await LoadAuditEventAsync(connection, result.AuditEventId!);
        Assert.NotNull(auditEvent);
        Assert.Equal(caseResult.CaseId, auditEvent.CaseId);
        Assert.Equal("source_registered", auditEvent.ActionType);
        Assert.Equal("source_import", auditEvent.EntityType);
        Assert.Equal(result.SourceImportId, auditEvent.EntityId);
        Assert.Equal("Source registered.", auditEvent.Summary);
        Assert.Contains(result.SourceImportId, auditEvent.NewValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain(sourceContent, auditEvent.NewValueJson, StringComparison.Ordinal);

        var auditLogger = new SqliteAuditLogger(BuildConnectionString(caseResult.DatabasePath));
        var verification = await auditLogger.VerifyChainAsync(caseResult.CaseId, correlationId: "source-register-e2e-verify");

        Assert.True(verification.IsValid);
        Assert.Equal(2, verification.CheckedEventCount);
        Assert.Equal(AuditChainFailureCodes.None, verification.FailureCode);
    }

    [Fact]
    public async Task RegisterAsync_FailsSafelyWhenSourceFileIsMissing()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);
        var missingFilePath = Path.Combine(tempDirectory.DirectoryPath, "missing-source.csv");

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => service.RegisterAsync(new RegisterSourceRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SelectedSourceFilePath = missingFilePath,
            SourceName = "Missing Synthetic Source",
            SourceType = "csv_messages",
            CorrelationId = "source-register-missing"
        }));

        Assert.Contains("selected source file path", exception.Message, StringComparison.OrdinalIgnoreCase);

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        Assert.Equal(0, await CountRowsAsync(connection, "source_imports"));
        Assert.Equal(1, await CountRowsAsync(connection, "audit_events"));

        var importsRootPath = Path.Combine(caseResult.PackageRootPath, "imports");
        Assert.Empty(Directory.GetDirectories(importsRootPath, "source_*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task RegisterAsync_SanitizesUnsafeOriginalFilenameOverride()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        var sourceFilePath = Path.Combine(tempDirectory.DirectoryPath, "plain.csv");
        await File.WriteAllTextAsync(sourceFilePath, "header\nvalue\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var service = CreateService();
        var result = await service.RegisterAsync(new RegisterSourceRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SelectedSourceFilePath = sourceFilePath,
            SourceName = "Sanitized Synthetic Source",
            SourceType = "csv_messages",
            OriginalFilenameOverride = "..\\unsafe<>name?.csv",
            CorrelationId = "source-register-sanitize"
        });

        Assert.Equal("unsafe-name-.csv", result.OriginalFilename);
        Assert.Equal("unsafe-name-.csv", Path.GetFileName(result.StoredFilePath));
        Assert.DoesNotContain("..", result.StoredFilePath, StringComparison.Ordinal);
        Assert.DoesNotContain("<", result.StoredFilePath, StringComparison.Ordinal);
        Assert.DoesNotContain(">", result.StoredFilePath, StringComparison.Ordinal);
        Assert.DoesNotContain("?", result.StoredFilePath, StringComparison.Ordinal);
        Assert.DoesNotContain("\\..\\", ToRelativePath(caseResult.PackageRootPath, result.StoredFilePath), StringComparison.Ordinal);

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        var storedSourceImport = await LoadSourceImportAsync(connection, result.SourceImportId);
        Assert.NotNull(storedSourceImport);
        Assert.Equal("unsafe-name-.csv", storedSourceImport.OriginalFilename);
    }

    [Fact]
    public async Task RegisterAsync_EmitsEvidenceSafeStructuredLogsForSuccessAndFailure()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        const string sensitiveToken = "TOP_SECRET_EVIDENCE_CONTENT";
        var sourceFilePath = Path.Combine(tempDirectory.DirectoryPath, "evidence-safe.csv");
        await File.WriteAllTextAsync(
            sourceFilePath,
            $"timestamp,message_body\n2026-04-01T12:00:00Z,{sensitiveToken}\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var logger = new TestLogger<SqliteSourceRegistrationService>();
        var service = CreateService(logger: logger);

        await service.RegisterAsync(new RegisterSourceRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SelectedSourceFilePath = sourceFilePath,
            SourceName = sensitiveToken,
            SourceType = "csv_messages",
            Notes = sensitiveToken,
            SourceMetadataJson = $"{{\"token\":\"{sensitiveToken}\"}}",
            CorrelationId = "source-register-log-success"
        });

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.RegisterAsync(new RegisterSourceRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SelectedSourceFilePath = Path.Combine(tempDirectory.DirectoryPath, $"{sensitiveToken}.csv"),
            SourceName = sensitiveToken,
            SourceType = "csv_messages",
            CorrelationId = "source-register-log-failure"
        }));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source registration started.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source folder created.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source file copied.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source file hashed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source manifest written.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source imports row inserted.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source registration audit event written.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source registration completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Source registration failed.", StringComparison.Ordinal));
        Assert.All(logger.Entries, entry => Assert.DoesNotContain(sensitiveToken, entry.Message, StringComparison.Ordinal));
        Assert.All(
            logger.Entries,
            entry =>
            {
                Assert.True(entry.State.ContainsKey("Operation"));
                Assert.True(entry.State.ContainsKey("CorrelationId"));
                Assert.True(entry.State.ContainsKey("CaseId"));
            });
    }

    private static async Task<CreateCaseResult> CreateCaseAsync(string parentDirectoryPath)
    {
        var caseService = new SqliteCaseService();
        return await caseService.CreateAsync(new CreateCaseRequest
        {
            CaseNumber = "DL-SRC-001",
            Title = "Synthetic Source Registration Case",
            ParentDirectoryPath = parentDirectoryPath,
            CorrelationId = "source-registration-case-create"
        });
    }

    private static SqliteSourceRegistrationService CreateService(
        TestLogger<SqliteSourceRegistrationService>? logger = null,
        IFileHashService? fileHashService = null)
    {
        return new SqliteSourceRegistrationService(
            fileHashService ?? new DeterministicSha256FileHashService(),
            new SqliteSourceImportRepository(),
            logger: logger);
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

    private static string ComputeSha256Hex(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? string.Empty : reader.GetString(5));
    }

    private static async Task<StoredSourceImport?> LoadSourceImportAsync(SqliteConnection connection, string sourceImportId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                case_id,
                source_name,
                source_type,
                platform,
                original_filename,
                original_file_path,
                stored_file_path,
                file_size_bytes,
                file_sha256,
                import_status,
                record_count,
                warning_count,
                notes,
                source_metadata_json
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
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    private static string ToRelativePath(string rootPath, string fullPath)
    {
        return Path.GetRelativePath(rootPath, fullPath).Replace('\\', '/');
    }

    private sealed record StoredAuditEvent(
        string CaseId,
        string ActionType,
        string EntityType,
        string EntityId,
        string Summary,
        string NewValueJson);

    private sealed record StoredSourceImport(
        string CaseId,
        string SourceName,
        string SourceType,
        string? Platform,
        string OriginalFilename,
        string? OriginalFilePath,
        string? StoredFilePath,
        long? FileSizeBytes,
        string FileSha256,
        string ImportStatus,
        int RecordCount,
        int WarningCount,
        string? Notes,
        string? SourceMetadataJson);

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
        public List<LogEntry> Entries { get; } = new();

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
