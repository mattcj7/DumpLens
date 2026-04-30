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
using DumpLens.Application.Sources;
using DumpLens.Application.Timestamps;
using DumpLens.Ingestion.Csv;
using DumpLens.Persistence.Cases;
using DumpLens.Persistence.Conversations;
using DumpLens.Persistence.MessageImports;
using DumpLens.Persistence.Sources;
using DumpLens.Tests.Integration.CasePackages;
using Microsoft.Data.Sqlite;

namespace DumpLens.Tests.Integration.Conversations;

public sealed class SqliteConversationReaderTests
{
    [Fact]
    public async Task GetSummariesAndThreadAsync_ReturnsOrderedSummariesMessagesAndSafeSourceContext()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        await ImportCsvMessagesAsync(
            caseResult,
            "reader-a.csv",
            "Synthetic Reader Source A",
            [
                new CsvMessageRow("2026-04-01T14:00:00Z", "555-700-0001", "555-700-0002", "Reader thread one", "sms", "outgoing", "thread-zulu", "msg-101"),
                new CsvMessageRow("2026-04-01T14:00:00Z", "555-700-0002", "555-700-0001", "Reader thread two", "sms", "incoming", "thread-zulu", "msg-102"),
                new CsvMessageRow("2026-04-01T14:05:00Z", "555-700-0001", "555-700-0002", "Reader thread three", "sms", "outgoing", "thread-zulu", "msg-103")
            ]);

        await ImportCsvMessagesAsync(
            caseResult,
            "reader-b.csv",
            "Synthetic Reader Source B",
            [
                new CsvMessageRow("2026-04-01T16:00:00Z", "alpha@example.test", "bravo@example.test", "Reader second conversation one", "signal", "outgoing", "thread-alpha", "msg-201"),
                new CsvMessageRow("2026-04-01T16:05:00Z", "bravo@example.test", "alpha@example.test", "Reader second conversation two", "signal", "incoming", "thread-alpha", "msg-202")
            ]);

        await RewriteMessageOrderingFieldsAsync(
            caseResult.DatabasePath,
            ("msg-101", "2026-04-01T14:00:00Z", "2026-04-01T14:00:01Z"),
            ("msg-102", "2026-04-01T14:00:00Z", "2026-04-01T14:00:02Z"),
            ("msg-103", "2026-04-01T14:05:00Z", "2026-04-01T14:05:00Z"));

        var builder = new SqliteConversationBuilderService();
        await builder.BuildAsync(new BuildConversationsRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CorrelationId = "conversation-reader-build"
        });

        var reader = new SqliteConversationReader();
        var summaries = await reader.GetSummariesAsync(new LoadConversationSummariesRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath
        });

        Assert.Equal(2, summaries.Count);
        Assert.Equal(2, summaries[0].MessageCount);
        Assert.Equal("signal", summaries[0].Platform);
        Assert.Equal(3, summaries[1].MessageCount);
        Assert.Equal("sms", summaries[1].Platform);
        Assert.True(summaries[0].EndTimeUtc > summaries[1].EndTimeUtc);

        var threadSummary = summaries.Single(summary => summary.MessageCount == 3);
        var thread = await reader.GetThreadAsync(new LoadConversationThreadRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            ConversationId = threadSummary.ConversationId
        });

        Assert.NotNull(thread);
        Assert.Equal(3, thread!.Messages.Count);
        Assert.Equal(
            ["msg-101", "msg-102", "msg-103"],
            thread.Messages.Select(message => Assert.IsType<string>(message.SourceContext?.ProviderMessageId)).ToArray());

        Assert.All(thread.Messages, message => Assert.False(string.IsNullOrWhiteSpace(message.SenderDisplayLabel)));
        Assert.All(thread.Messages, message => Assert.NotEmpty(message.RecipientDisplayLabels));
        Assert.Equal("Reader thread one", thread.Messages[0].MessageBody);
        Assert.Equal("Reader thread two", thread.Messages[1].MessageBody);
        Assert.Equal("Reader thread three", thread.Messages[2].MessageBody);

        var sourceContext = thread.Messages[0].SourceContext;
        Assert.NotNull(sourceContext);
        Assert.Equal("Synthetic Reader Source A", sourceContext!.SourceName);
        Assert.Equal("csv_messages", sourceContext.SourceType);
        Assert.Equal("sms", sourceContext.Platform);
        Assert.Equal("reader-a.csv", sourceContext.OriginalFilename);
        Assert.False(string.IsNullOrWhiteSpace(sourceContext.SourceImportId));
        Assert.False(string.IsNullOrWhiteSpace(sourceContext.SourceArtifactId));
        Assert.Contains("row:", sourceContext.ArtifactLocator, StringComparison.Ordinal);
        Assert.Equal("thread-zulu", sourceContext.SourceThreadId);
        Assert.Equal("msg-101", sourceContext.ProviderMessageId);
        Assert.Equal(12, sourceContext.MessageHashPrefix!.Length);
    }

    [Fact]
    public async Task GetThreadAsync_For_Missing_Conversation_Returns_Null()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);
        var reader = new SqliteConversationReader();

        var thread = await reader.GetThreadAsync(new LoadConversationThreadRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            ConversationId = "missing-conversation"
        });

        Assert.Null(thread);
    }

    private static async Task RewriteMessageOrderingFieldsAsync(
        string databasePath,
        params (string ProviderMessageId, string EventTimeUtc, string CreatedAtUtc)[] updates)
    {
        await using var connection = new SqliteConnection(BuildConnectionString(databasePath));
        await connection.OpenAsync();

        foreach (var update in updates)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE messages
                SET event_time_utc = $eventTimeUtc,
                    created_at_utc = $createdAtUtc,
                    updated_at_utc = $createdAtUtc
                WHERE provider_message_id = $providerMessageId;
                """;
            command.Parameters.AddWithValue("$providerMessageId", update.ProviderMessageId);
            command.Parameters.AddWithValue("$eventTimeUtc", update.EventTimeUtc);
            command.Parameters.AddWithValue("$createdAtUtc", update.CreatedAtUtc);
            await command.ExecuteNonQueryAsync();
        }
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

    private static async Task<CreateCaseResult> CreateCaseAsync(string parentDirectoryPath)
    {
        var caseService = new SqliteCaseService();
        return await caseService.CreateAsync(new CreateCaseRequest
        {
            CaseNumber = "DL-CONV-RDR-001",
            Title = "Synthetic Conversation Reader Case",
            ParentDirectoryPath = parentDirectoryPath,
            CorrelationId = "conversation-reader-case-create"
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
}
