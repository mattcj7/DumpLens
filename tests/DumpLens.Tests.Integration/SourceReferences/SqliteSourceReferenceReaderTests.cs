using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DumpLens.Application.Cases;
using DumpLens.Application.FileHashing;
using DumpLens.Application.Identities;
using DumpLens.Application.Imports;
using DumpLens.Application.MessageImports;
using DumpLens.Application.SourceReferences;
using DumpLens.Application.Sources;
using DumpLens.Application.Timestamps;
using DumpLens.Ingestion.Csv;
using DumpLens.Persistence.Cases;
using DumpLens.Persistence.MessageImports;
using DumpLens.Persistence.SourceReferences;
using DumpLens.Persistence.Sources;
using DumpLens.Tests.Integration.CasePackages;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DumpLens.Tests.Integration.SourceReferences;

public sealed class SqliteSourceReferenceReaderTests
{
    [Fact]
    public async Task LoadAsync_Returns_Safe_Source_Artifact_And_Message_Reference_Detail()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);
        var importResult = await ImportCsvMessagesAsync(
            caseResult,
            fileName: "source-reference.csv",
            sourceName: "Synthetic Reference Source",
            rows:
            [
                new CsvMessageRow("2026-05-04T12:00:00Z", "555-700-0001", "555-700-0002", "Synthetic body", "sms", "outgoing", "thread-ref", "provider-ref-001")
            ]);

        var reader = new SqliteSourceReferenceReader();
        var detail = await reader.LoadAsync(new LoadSourceReferenceRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SourceImportId = importResult.SourceImportId,
            SourceArtifactId = importResult.SourceArtifactId,
            MessageId = importResult.MessageId,
            CorrelationId = "source-reference-load-success"
        });

        Assert.NotNull(detail);
        Assert.Equal(caseResult.CaseId, detail!.CaseId);
        Assert.Equal(importResult.SourceImportId, detail.SourceImportId);
        Assert.Equal("Synthetic Reference Source", detail.SourceName);
        Assert.Equal("csv_messages", detail.SourceType);
        Assert.Equal("sms", detail.Platform);
        Assert.Equal("imported", detail.ImportStatus);
        Assert.Equal("source-reference.csv", detail.OriginalFilename);
        Assert.False(Path.IsPathRooted(detail.StoredRelativePath!));
        Assert.StartsWith("imports/source_", detail.StoredRelativePath!, StringComparison.Ordinal);
        Assert.NotNull(detail.FileSizeBytes);
        Assert.Equal(64, detail.FileSha256.Length);
        Assert.True(detail.HasSourceMetadata);

        Assert.NotNull(detail.ArtifactReference);
        Assert.Equal(importResult.SourceArtifactId, detail.ArtifactReference!.SourceArtifactId);
        Assert.Equal("message_row", detail.ArtifactReference.ArtifactType);
        Assert.Equal("row:2", detail.ArtifactReference.ArtifactLocator);
        Assert.True(detail.ArtifactReference.HasOriginalMetadata);

        Assert.NotNull(detail.MessageReference);
        Assert.Equal(importResult.MessageId, detail.MessageReference!.MessageId);
        Assert.Equal(importResult.SourceArtifactId, detail.MessageReference.SourceArtifactId);
        Assert.Equal("provider-ref-001", detail.MessageReference.ProviderMessageId);
        Assert.Equal("thread-ref", detail.MessageReference.SourceThreadId);
        Assert.Equal("present", detail.MessageReference.DeletedStatus);
        Assert.Equal(12, detail.MessageReference.MessageHashPrefix!.Length);
        Assert.True(detail.MessageReference.HasOriginalMetadata);
    }

    [Fact]
    public async Task LoadAsync_With_Missing_Artifact_And_Message_Returns_Safe_Partial_Detail()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);
        var importResult = await ImportCsvMessagesAsync(
            caseResult,
            fileName: "partial-reference.csv",
            sourceName: "Synthetic Partial Reference Source",
            rows:
            [
                new CsvMessageRow("2026-05-04T13:00:00Z", "555-710-0001", "555-710-0002", "Synthetic partial body", "sms", "incoming", "thread-partial", "provider-partial-001")
            ]);

        var reader = new SqliteSourceReferenceReader();
        var detail = await reader.LoadAsync(new LoadSourceReferenceRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SourceImportId = importResult.SourceImportId,
            SourceArtifactId = "missing-artifact-id",
            MessageId = "missing-message-id",
            CorrelationId = "source-reference-load-partial"
        });

        Assert.NotNull(detail);
        Assert.True(detail!.WasArtifactReferenceRequested);
        Assert.True(detail.WasMessageReferenceRequested);
        Assert.Null(detail.ArtifactReference);
        Assert.Null(detail.MessageReference);
    }

    [Fact]
    public async Task LoadAsync_Emits_Evidence_Safe_Logs_For_Success_Missing_And_Failure()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        const string sensitiveToken = "TOP_SECRET_SOURCE_REFERENCE_TOKEN";
        var importResult = await ImportCsvMessagesAsync(
            caseResult,
            fileName: "sensitive-reference.csv",
            sourceName: sensitiveToken,
            rows:
            [
                new CsvMessageRow("2026-05-04T14:00:00Z", "555-720-0001", "555-720-0002", sensitiveToken, "sms", "outgoing", "thread-sensitive", "provider-sensitive-001")
            ]);

        var logger = new TestLogger<SqliteSourceReferenceReader>();
        var reader = new SqliteSourceReferenceReader(logger);

        var loadedDetail = await reader.LoadAsync(new LoadSourceReferenceRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SourceImportId = importResult.SourceImportId,
            SourceArtifactId = importResult.SourceArtifactId,
            MessageId = importResult.MessageId,
            CorrelationId = "source-reference-log-success"
        });

        Assert.NotNull(loadedDetail);

        var missingDetail = await reader.LoadAsync(new LoadSourceReferenceRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SourceImportId = "missing-source-import-id",
            CorrelationId = "source-reference-log-missing"
        });

        Assert.Null(missingDetail);

        await DropSourceImportsTableAsync(caseResult.DatabasePath);
        await Assert.ThrowsAsync<SqliteException>(() => reader.LoadAsync(new LoadSourceReferenceRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SourceImportId = importResult.SourceImportId,
            SourceArtifactId = importResult.SourceArtifactId,
            MessageId = importResult.MessageId,
            CorrelationId = "source-reference-log-failure"
        }));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source reference requested.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Source reference loaded.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Source reference missing.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Source reference load failed.", StringComparison.Ordinal));

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

    private static async Task<CreateCaseResult> CreateCaseAsync(string parentDirectoryPath)
    {
        var caseService = new SqliteCaseService();
        return await caseService.CreateAsync(new CreateCaseRequest
        {
            CaseNumber = "DL-SRC-REF-001",
            Title = "Synthetic Source Reference Case",
            ParentDirectoryPath = parentDirectoryPath,
            CorrelationId = "source-reference-case-create"
        });
    }

    private static async Task<ImportedReferenceIds> ImportCsvMessagesAsync(
        CreateCaseResult caseResult,
        string fileName,
        string sourceName,
        IReadOnlyList<CsvMessageRow> rows)
    {
        var csvFilePath = Path.Combine(caseResult.PackageRootPath, fileName);
        await File.WriteAllTextAsync(csvFilePath, BuildCsvContent(rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var registerResult = await RegisterSourceAsync(caseResult, csvFilePath, sourceName, $"register-{Path.GetFileNameWithoutExtension(fileName)}");
        var importService = CreateMessageImportService();
        await importService.ImportAsync(new ImportMessagesRequest
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
                ("message_id", ImportFieldNames.MessageId)),
            TimezoneAssumption = "Eastern Standard Time",
            CorrelationId = $"import-{Path.GetFileNameWithoutExtension(fileName)}"
        });

        return await LoadReferenceIdsAsync(caseResult.DatabasePath, registerResult.SourceImportId);
    }

    private static SqliteMessageImportService CreateMessageImportService()
    {
        return new SqliteMessageImportService(
            [new CsvSourceImporter()],
            CreateIdentityNormalizer(),
            CreateTimestampNormalizer());
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

    private static async Task<RegisterSourceResult> RegisterSourceAsync(
        CreateCaseResult caseResult,
        string sourceFilePath,
        string sourceName,
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
            SourceType = "csv_messages",
            Platform = "sms",
            SourceMetadataJson = "{\"source\":\"present\"}",
            CorrelationId = correlationId
        });
    }

    private static async Task<ImportedReferenceIds> LoadReferenceIdsAsync(string databasePath, string sourceImportId)
    {
        await using var connection = new SqliteConnection(BuildConnectionString(databasePath));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                m.id,
                m.source_artifact_id
            FROM messages AS m
            WHERE m.source_import_id = $sourceImportId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sourceImportId", sourceImportId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new ImportedReferenceIds(
            SourceImportId: sourceImportId,
            SourceArtifactId: reader.GetString(1),
            MessageId: reader.GetString(0));
    }

    private static async Task DropSourceImportsTableAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(BuildConnectionString(databasePath));
        await connection.OpenAsync();

        await using (var disableCommand = connection.CreateCommand())
        {
            disableCommand.CommandText = "PRAGMA foreign_keys = OFF;";
            await disableCommand.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE source_imports;";
        await command.ExecuteNonQueryAsync();
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

    private static string BuildCsvContent(IReadOnlyList<CsvMessageRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("timestamp,sender,recipient,message_body,platform,direction,thread_id,message_id");

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(
                ",",
                [
                    EscapeCsvValue(row.Timestamp),
                    EscapeCsvValue(row.Sender),
                    EscapeCsvValue(row.Recipient),
                    EscapeCsvValue(row.MessageBody),
                    EscapeCsvValue(row.Platform),
                    EscapeCsvValue(row.Direction),
                    EscapeCsvValue(row.ThreadId),
                    EscapeCsvValue(row.MessageId)
                ]));
        }

        return builder.ToString();
    }

    private static string EscapeCsvValue(string? value)
    {
        var text = value ?? string.Empty;
        if (text.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
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

    private sealed record CsvMessageRow(
        string Timestamp,
        string Sender,
        string Recipient,
        string MessageBody,
        string Platform,
        string Direction,
        string? ThreadId,
        string MessageId);

    private sealed record ImportedReferenceIds(
        string SourceImportId,
        string SourceArtifactId,
        string MessageId);

    private sealed class DeterministicSha256FileHashService : IFileHashService
    {
        public async Task<FileHashResult> ComputeHashAsync(
            FileHashRequest request,
            CancellationToken cancellationToken = default)
        {
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
                cancellationToken);

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
