using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DumpLens.Application.Cases;
using DumpLens.Application.Conversations;
using DumpLens.Application.FileHashing;
using DumpLens.Application.Identities;
using DumpLens.Application.Imports;
using DumpLens.Application.MessageImports;
using DumpLens.Application.Search;
using DumpLens.Application.Sources;
using DumpLens.Application.Timestamps;
using DumpLens.Ingestion.Csv;
using DumpLens.Persistence.Cases;
using DumpLens.Persistence.Conversations;
using DumpLens.Persistence.MessageImports;
using DumpLens.Persistence.Search;
using DumpLens.Persistence.Sources;
using DumpLens.Tests.Integration.CasePackages;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DumpLens.Tests.Integration.Search;

public sealed class SqliteMessageSearchIndexServiceTests
{
    [Fact]
    public async Task RebuildAndSearchAsync_ReturnsCaseScopedTraceableResults()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();

        var firstCase = await CreateCaseAsync(tempDirectory.DirectoryPath, "DL-SRCH-001", "Synthetic Search Case One");
        var secondCase = await CreateCaseAsync(tempDirectory.DirectoryPath, "DL-SRCH-002", "Synthetic Search Case Two");

        var firstSourceImportId = await ImportCsvMessagesAsync(
            firstCase,
            "case-one.csv",
            "Synthetic Search Source One",
            [
                new CsvMessageRow("2026-04-01T12:00:00Z", "555-510-0001", "555-510-0002", "Orchid trail briefing", "sms", "outgoing", "thread-search-1", "msg-search-001"),
                new CsvMessageRow("2026-04-01T12:05:00Z", "555-510-0002", "555-510-0001", "Follow the orchid lead", "sms", "incoming", "thread-search-1", "msg-search-002"),
                new CsvMessageRow("2026-04-01T12:10:00Z", "555-510-0001", "555-510-0002", "Separate synthetic body", "sms", "outgoing", "thread-search-1", "msg-search-003")
            ]);

        await ImportCsvMessagesAsync(
            secondCase,
            "case-two.csv",
            "Synthetic Search Source Two",
            [
                new CsvMessageRow("2026-04-02T12:00:00Z", "555-520-0001", "555-520-0002", "Orchid appears in another case", "sms", "outgoing", "thread-search-2", "msg-search-101")
            ]);

        var conversationBuilder = new SqliteConversationBuilderService();
        await conversationBuilder.BuildAsync(new BuildConversationsRequest
        {
            CaseId = firstCase.CaseId,
            CaseDatabasePath = firstCase.DatabasePath,
            CorrelationId = "search-build-case-one"
        });

        await conversationBuilder.BuildAsync(new BuildConversationsRequest
        {
            CaseId = secondCase.CaseId,
            CaseDatabasePath = secondCase.DatabasePath,
            CorrelationId = "search-build-case-two"
        });

        var service = new SqliteMessageSearchIndexService();

        var firstRebuild = await service.RebuildAsync(new RebuildMessageSearchIndexRequest
        {
            CaseId = firstCase.CaseId,
            CaseDatabasePath = firstCase.DatabasePath,
            CorrelationId = "search-rebuild-case-one"
        });

        var secondRebuild = await service.RebuildAsync(new RebuildMessageSearchIndexRequest
        {
            CaseId = secondCase.CaseId,
            CaseDatabasePath = secondCase.DatabasePath,
            CorrelationId = "search-rebuild-case-two"
        });

        Assert.Equal(3, firstRebuild.IndexedCount);
        Assert.Equal(1, secondRebuild.IndexedCount);

        var firstSearch = await service.SearchAsync(new SearchMessagesRequest
        {
            CaseId = firstCase.CaseId,
            CaseDatabasePath = firstCase.DatabasePath,
            QueryText = "orchid",
            CorrelationId = "search-query-case-one"
        });

        var secondSearch = await service.SearchAsync(new SearchMessagesRequest
        {
            CaseId = secondCase.CaseId,
            CaseDatabasePath = secondCase.DatabasePath,
            QueryText = "orchid",
            CorrelationId = "search-query-case-two"
        });

        Assert.True(firstSearch.IsQueryValid);
        Assert.Equal(2, firstSearch.ResultCount);
        Assert.Equal(2, firstSearch.Results.Count);
        Assert.All(firstSearch.Results, result => Assert.Equal(firstCase.CaseId, result.CaseId));
        Assert.All(firstSearch.Results, result => Assert.False(string.IsNullOrWhiteSpace(result.SourceImportId)));
        Assert.All(firstSearch.Results, result => Assert.False(string.IsNullOrWhiteSpace(result.SourceArtifactId)));
        Assert.All(firstSearch.Results, result => Assert.Equal("sms", result.Platform));
        Assert.All(firstSearch.Results, result => Assert.Equal("present", result.DeletedStatus));
        Assert.All(firstSearch.Results, result => Assert.False(string.IsNullOrWhiteSpace(result.ProviderMessageId)));
        Assert.All(firstSearch.Results, result => Assert.False(string.IsNullOrWhiteSpace(result.SourceThreadId)));
        Assert.All(firstSearch.Results, result => Assert.NotNull(result.EventTimeUtc));
        Assert.All(firstSearch.Results, result => Assert.NotNull(result.ConversationId));
        Assert.All(firstSearch.Results, result => Assert.NotNull(result.Rank));
        Assert.Contains(firstSearch.Results, result => result.Snippet?.Contains("[[Orchid]]", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(firstSearch.Results, result => result.MessageId.Length > 0);

        Assert.True(secondSearch.IsQueryValid);
        Assert.Equal(1, secondSearch.ResultCount);
        Assert.Single(secondSearch.Results);
        Assert.Equal(secondCase.CaseId, secondSearch.Results[0].CaseId);
        Assert.DoesNotContain(firstSourceImportId, secondSearch.Results.Select(static result => result.SourceImportId));
    }

    [Fact]
    public async Task RebuildAsync_Twice_IsIdempotentAndDoesNotDuplicateIndexedRows()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath, "DL-SRCH-IDEM-001", "Synthetic Search Idempotent Case");

        await ImportCsvMessagesAsync(
            caseResult,
            "idempotent.csv",
            "Synthetic Search Idempotent Source",
            [
                new CsvMessageRow("2026-04-03T12:00:00Z", "555-530-0001", "555-530-0002", "Anchor body one", "sms", "outgoing", "thread-idem", "msg-idem-001"),
                new CsvMessageRow("2026-04-03T12:05:00Z", "555-530-0002", "555-530-0001", "Anchor body two", "sms", "incoming", "thread-idem", "msg-idem-002")
            ]);

        var service = new SqliteMessageSearchIndexService();

        var firstRebuild = await service.RebuildAsync(new RebuildMessageSearchIndexRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CorrelationId = "search-rebuild-first"
        });

        var secondRebuild = await service.RebuildAsync(new RebuildMessageSearchIndexRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CorrelationId = "search-rebuild-second"
        });

        Assert.Equal(2, firstRebuild.IndexedCount);
        Assert.Equal(2, secondRebuild.IndexedCount);

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        var indexedRowCount = await CountIndexedRowsAsync(connection, caseResult.CaseId);
        Assert.Equal(2, indexedRowCount);

        var searchResult = await service.SearchAsync(new SearchMessagesRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            QueryText = "anchor",
            CorrelationId = "search-query-idempotent"
        });

        Assert.True(searchResult.IsQueryValid);
        Assert.Equal(2, searchResult.ResultCount);
        Assert.Equal(2, searchResult.Results.Select(static result => result.MessageId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task SearchAsync_EmptyAndSpecialCharacterQueriesReturnSafeResponses()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath, "DL-SRCH-SAFE-001", "Synthetic Search Safe Query Case");

        await ImportCsvMessagesAsync(
            caseResult,
            "safe-query.csv",
            "Synthetic Search Safe Query Source",
            [
                new CsvMessageRow("2026-04-04T12:00:00Z", "555-540-0001", "555-540-0002", "Marker body with orchid token", "sms", "outgoing", "thread-safe", "msg-safe-001")
            ]);

        var service = new SqliteMessageSearchIndexService();
        await service.RebuildAsync(new RebuildMessageSearchIndexRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CorrelationId = "search-rebuild-safe"
        });

        var emptyQueryResult = await service.SearchAsync(new SearchMessagesRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            QueryText = "   ",
            CorrelationId = "search-empty-query"
        });

        var punctuationOnlyResult = await service.SearchAsync(new SearchMessagesRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            QueryText = "\"\"\" ((( ))) ###",
            CorrelationId = "search-punctuation-query"
        });

        var sanitizedSpecialCharacterResult = await service.SearchAsync(new SearchMessagesRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            QueryText = "orchid!!!",
            CorrelationId = "search-special-character-query"
        });

        Assert.False(emptyQueryResult.IsQueryValid);
        Assert.Equal(MessageSearchValidationCodes.EmptyQuery, emptyQueryResult.ValidationErrorCode);
        Assert.Equal(0, emptyQueryResult.ResultCount);

        Assert.False(punctuationOnlyResult.IsQueryValid);
        Assert.Equal(MessageSearchValidationCodes.UnsupportedQuery, punctuationOnlyResult.ValidationErrorCode);
        Assert.Equal(0, punctuationOnlyResult.ResultCount);

        Assert.True(sanitizedSpecialCharacterResult.IsQueryValid);
        Assert.Single(sanitizedSpecialCharacterResult.Results);
        Assert.Equal("msg-safe-001", sanitizedSpecialCharacterResult.Results[0].ProviderMessageId);
    }

    [Fact]
    public async Task RebuildAndSearchAsync_EmitEvidenceSafeStructuredLogs()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        const string sensitiveToken = "TOP_SECRET_SEARCH_TOKEN";

        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath, "DL-SRCH-LOG-001", "Synthetic Search Log Case");
        await ImportCsvMessagesAsync(
            caseResult,
            "logging.csv",
            sensitiveToken,
            [
                new CsvMessageRow("2026-04-05T12:00:00Z", sensitiveToken, sensitiveToken, sensitiveToken, "sms", "outgoing", "thread-log", "msg-log-001")
            ]);

        var logger = new TestLogger<SqliteMessageSearchIndexService>();
        var service = new SqliteMessageSearchIndexService(logger);

        await service.RebuildAsync(new RebuildMessageSearchIndexRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CorrelationId = "search-log-rebuild-success"
        });

        await service.SearchAsync(new SearchMessagesRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            QueryText = sensitiveToken,
            CorrelationId = "search-log-search-success"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebuildAsync(new RebuildMessageSearchIndexRequest
        {
            CaseId = "missing-case-id",
            CaseDatabasePath = caseResult.DatabasePath,
            CorrelationId = "search-log-rebuild-failure"
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchAsync(new SearchMessagesRequest
        {
            CaseId = "missing-case-id",
            CaseDatabasePath = caseResult.DatabasePath,
            QueryText = "safe-query",
            CorrelationId = "search-log-search-failure"
        }));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Message search index rebuild started.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Message search index rebuild completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Message search index rebuild failed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Message search requested.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Message search completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Message search failed.", StringComparison.Ordinal));

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
            });
    }

    private static async Task<CreateCaseResult> CreateCaseAsync(string parentDirectoryPath, string caseNumber, string title)
    {
        var caseService = new SqliteCaseService();
        return await caseService.CreateAsync(new CreateCaseRequest
        {
            CaseNumber = caseNumber,
            Title = title,
            ParentDirectoryPath = parentDirectoryPath,
            CorrelationId = $"create-{caseNumber}"
        });
    }

    private static async Task<string> ImportCsvMessagesAsync(
        CreateCaseResult caseResult,
        string fileName,
        string sourceName,
        IReadOnlyList<CsvMessageRow> rows)
    {
        var csvFilePath = Path.Combine(caseResult.PackageRootPath, fileName);
        await File.WriteAllTextAsync(csvFilePath, BuildCsvContent(rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var registerResult = await RegisterSourceAsync(
            caseResult,
            csvFilePath,
            sourceName,
            correlationId: $"register-{Path.GetFileNameWithoutExtension(fileName)}");

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

        return registerResult.SourceImportId;
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
            CorrelationId = correlationId
        });
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

    private static async Task<int> CountIndexedRowsAsync(SqliteConnection connection, string caseId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM message_search_index
            WHERE case_id = $caseId;
            """;
        command.Parameters.AddWithValue("$caseId", caseId);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
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
