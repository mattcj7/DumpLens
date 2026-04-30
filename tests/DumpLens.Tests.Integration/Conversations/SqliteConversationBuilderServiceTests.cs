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
using Microsoft.Extensions.Logging;

namespace DumpLens.Tests.Integration.Conversations;

public sealed class SqliteConversationBuilderServiceTests
{
    [Fact]
    public async Task BuildAsync_SameThreadMessagesBecomeOneConversationWithParticipantsAndAssignments()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        var sourceImportId = await ImportCsvMessagesAsync(
            caseResult,
            "same-thread.csv",
            "Synthetic Same Thread",
            [
                new CsvMessageRow("2026-04-01T12:00:00Z", "555-100-0001", "555-100-0002", "Thread message one", "sms", "outgoing", "thread-alpha", "msg-001"),
                new CsvMessageRow("2026-04-01T12:05:00Z", "555-100-0002", "555-100-0001", "Thread message two", "sms", "incoming", "thread-alpha", "msg-002"),
                new CsvMessageRow("2026-04-01T12:09:00Z", "555-100-0001", "555-100-0002", "Thread message three", "sms", "outgoing", "thread-alpha", "msg-003")
            ]);

        var service = CreateConversationBuilderService();
        var result = await service.BuildAsync(new BuildConversationsRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CorrelationId = "conversation-build-same-thread"
        });

        Assert.Equal(caseResult.CaseId, result.CaseId);
        Assert.Equal(1, result.ConversationCountCreated);
        Assert.Equal(0, result.ConversationCountUpdated);
        Assert.Equal(2, result.ParticipantCountCreated);
        Assert.Equal(3, result.MessageCountAssigned);
        Assert.Equal(0, result.UnassignedMessageCount);
        Assert.Single(result.ConversationSummaries);

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        var conversations = await LoadConversationsAsync(connection);
        Assert.Single(conversations);

        var conversation = conversations[0];
        Assert.Equal("sms", conversation.Platform);
        Assert.Equal(3, conversation.MessageCount);
        Assert.Equal(1, conversation.SourceCount);
        Assert.Equal("[\"thread-alpha\"]", conversation.SourceThreadKeysJson);
        Assert.NotNull(conversation.StartTimeUtc);
        Assert.NotNull(conversation.EndTimeUtc);
        Assert.Equal("not_started", conversation.ReconciliationStatus);
        Assert.Equal("unreviewed", conversation.ReviewStatus);

        var messages = await LoadMessagesAsync(connection);
        Assert.Equal(3, messages.Count);
        Assert.All(messages, message => Assert.Equal(conversation.Id, message.ConversationId));
        Assert.All(messages, message => Assert.Equal(sourceImportId, message.SourceImportId));

        var participants = await LoadConversationParticipantsAsync(connection, conversation.Id);
        Assert.Equal(2, participants.Count);
        Assert.All(participants, participant => Assert.Null(participant.PersonId));
        Assert.All(participants, participant => Assert.Equal(sourceImportId, participant.SourceImportId));
    }

    [Fact]
    public async Task BuildAsync_DifferentThreadIdsCreateDifferentConversations()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        await ImportCsvMessagesAsync(
            caseResult,
            "different-threads.csv",
            "Synthetic Different Threads",
            [
                new CsvMessageRow("2026-04-01T13:00:00Z", "555-200-0001", "555-200-0002", "Thread A one", "sms", "outgoing", "thread-a", "msg-101"),
                new CsvMessageRow("2026-04-01T13:05:00Z", "555-200-0001", "555-200-0002", "Thread A two", "sms", "outgoing", "thread-a", "msg-102"),
                new CsvMessageRow("2026-04-01T14:00:00Z", "555-200-0001", "555-200-0002", "Thread B one", "sms", "outgoing", "thread-b", "msg-201")
            ]);

        var service = CreateConversationBuilderService();
        var result = await service.BuildAsync(new BuildConversationsRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CorrelationId = "conversation-build-different-thread"
        });

        Assert.Equal(2, result.ConversationCountCreated);
        Assert.Equal(3, result.MessageCountAssigned);

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        var conversations = await LoadConversationsAsync(connection);
        Assert.Equal(2, conversations.Count);
        Assert.Contains(conversations, conversation => conversation.SourceThreadKeysJson == "[\"thread-a\"]" && conversation.MessageCount == 2);
        Assert.Contains(conversations, conversation => conversation.SourceThreadKeysJson == "[\"thread-b\"]" && conversation.MessageCount == 1);

        var messages = await LoadMessagesAsync(connection);
        var groupedByConversation = messages.GroupBy(message => message.ConversationId, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, groupedByConversation.Length);
        Assert.Contains(groupedByConversation, group => group.Count() == 2);
        Assert.Contains(groupedByConversation, group => group.Count() == 1);
    }

    [Fact]
    public async Task BuildAsync_MissingThreadIdsGroupByParticipantSet()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        await ImportCsvMessagesAsync(
            caseResult,
            "participant-set.csv",
            "Synthetic Participant Set",
            [
                new CsvMessageRow("2026-04-01T15:00:00Z", "alpha@example.test", "bravo@example.test", "Participant set one", "signal", "outgoing", null, "msg-301"),
                new CsvMessageRow("2026-04-01T15:05:00Z", "bravo@example.test", "alpha@example.test", "Participant set two", "signal", "incoming", null, "msg-302"),
                new CsvMessageRow("2026-04-01T15:10:00Z", "alpha@example.test", "charlie@example.test", "Participant set three", "signal", "outgoing", null, "msg-303")
            ]);

        var service = CreateConversationBuilderService();
        var result = await service.BuildAsync(new BuildConversationsRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CorrelationId = "conversation-build-participant-set"
        });

        Assert.Equal(2, result.ConversationCountCreated);
        Assert.Equal(4, result.ParticipantCountCreated);
        Assert.Equal(3, result.MessageCountAssigned);

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        var conversations = await LoadConversationsAsync(connection);
        Assert.Equal(2, conversations.Count);
        Assert.All(conversations, conversation => Assert.Equal("[]", conversation.SourceThreadKeysJson));
        Assert.Contains(conversations, conversation => conversation.MessageCount == 2);
        Assert.Contains(conversations, conversation => conversation.MessageCount == 1);

        var participants = await LoadAllConversationParticipantsAsync(connection);
        Assert.Equal(4, participants.Count);
    }

    [Fact]
    public async Task BuildAsync_SourceImportScopeOnlyProcessesRequestedSourceAndCanReuseExistingConversation()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        var firstSourceImportId = await ImportCsvMessagesAsync(
            caseResult,
            "scope-a.csv",
            "Scoped Source A",
            [
                new CsvMessageRow("2026-04-01T16:00:00Z", "555-300-0001", "555-300-0002", "Scoped A one", "sms", "outgoing", "shared-thread", "msg-401"),
                new CsvMessageRow("2026-04-01T16:03:00Z", "555-300-0002", "555-300-0001", "Scoped A two", "sms", "incoming", "shared-thread", "msg-402")
            ]);

        var secondSourceImportId = await ImportCsvMessagesAsync(
            caseResult,
            "scope-b.csv",
            "Scoped Source B",
            [
                new CsvMessageRow("2026-04-01T16:05:00Z", "555-300-0001", "555-300-0002", "Scoped B one", "sms", "outgoing", "shared-thread", "msg-403"),
                new CsvMessageRow("2026-04-01T16:07:00Z", "555-300-0002", "555-300-0001", "Scoped B two", "sms", "incoming", "shared-thread", "msg-404")
            ]);

        var service = CreateConversationBuilderService();

        var firstResult = await service.BuildAsync(new BuildConversationsRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            SourceImportId = firstSourceImportId,
            CorrelationId = "conversation-build-scope-a"
        });

        Assert.Equal(1, firstResult.ConversationCountCreated);
        Assert.Equal(2, firstResult.MessageCountAssigned);

        await using (var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath)))
        {
            await connection.OpenAsync();

            var messagesAfterFirstBuild = await LoadMessagesAsync(connection);
            Assert.Equal(2, messagesAfterFirstBuild.Count(message => message.SourceImportId == firstSourceImportId && message.ConversationId is not null));
            Assert.Equal(2, messagesAfterFirstBuild.Count(message => message.SourceImportId == secondSourceImportId && message.ConversationId is null));
        }

        var secondResult = await service.BuildAsync(new BuildConversationsRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            SourceImportId = secondSourceImportId,
            CorrelationId = "conversation-build-scope-b"
        });

        Assert.Equal(0, secondResult.ConversationCountCreated);
        Assert.Equal(1, secondResult.ConversationCountUpdated);
        Assert.Equal(2, secondResult.MessageCountAssigned);

        await using (var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath)))
        {
            await connection.OpenAsync();

            var conversations = await LoadConversationsAsync(connection);
            Assert.Single(conversations);
            Assert.Equal(2, conversations[0].SourceCount);
            Assert.Equal(4, conversations[0].MessageCount);

            var messagesAfterSecondBuild = await LoadMessagesAsync(connection);
            Assert.All(messagesAfterSecondBuild, message => Assert.Equal(conversations[0].Id, message.ConversationId));
        }
    }

    [Fact]
    public async Task BuildAsync_RebuildExistingIsIdempotentAndDoesNotDuplicateConversationsOrParticipants()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        await ImportCsvMessagesAsync(
            caseResult,
            "idempotent.csv",
            "Synthetic Idempotent",
            [
                new CsvMessageRow("2026-04-01T17:00:00Z", "555-400-0001", "555-400-0002", "Idempotent one", "sms", "outgoing", "idem-thread", "msg-501"),
                new CsvMessageRow("2026-04-01T17:05:00Z", "555-400-0002", "555-400-0001", "Idempotent two", "sms", "incoming", "idem-thread", "msg-502")
            ]);

        var service = CreateConversationBuilderService();
        var firstResult = await service.BuildAsync(new BuildConversationsRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CorrelationId = "conversation-build-idempotent-first"
        });

        var secondResult = await service.BuildAsync(new BuildConversationsRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            RebuildExisting = true,
            CorrelationId = "conversation-build-idempotent-second"
        });

        Assert.Equal(1, firstResult.ConversationCountCreated);
        Assert.Equal(0, secondResult.ConversationCountCreated);
        Assert.Equal(0, secondResult.ConversationCountUpdated);
        Assert.Equal(0, secondResult.ParticipantCountCreated);
        Assert.Equal(0, secondResult.MessageCountAssigned);
        Assert.Equal(0, secondResult.UnassignedMessageCount);

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        Assert.Equal(1, await CountRowsAsync(connection, "conversations"));
        Assert.Equal(2, await CountRowsAsync(connection, "conversation_participants"));

        var messages = await LoadMessagesAsync(connection);
        Assert.Equal(2, messages.Count);
        Assert.Single(messages.Select(message => message.ConversationId).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task BuildAsync_EmitsEvidenceSafeStructuredLogsForSuccessAndFailure()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        const string sensitiveToken = "TOP_SECRET_CONVERSATION_TOKEN";
        var sourceImportId = await ImportCsvMessagesAsync(
            caseResult,
            "logging.csv",
            sensitiveToken,
            [
                new CsvMessageRow("2026-04-01T18:00:00Z", sensitiveToken, sensitiveToken, sensitiveToken, "sms", "outgoing", "thread-log", "msg-601")
            ]);

        var logger = new TestLogger<SqliteConversationBuilderService>();
        var service = CreateConversationBuilderService(logger);

        await service.BuildAsync(new BuildConversationsRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            SourceImportId = sourceImportId,
            CorrelationId = "conversation-build-log-success"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildAsync(new BuildConversationsRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            SourceImportId = "missing-source-import",
            CorrelationId = "conversation-build-log-failure"
        }));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Conversation build started.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Candidate messages loaded.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Conversation groups created.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Conversation records upserted.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Participants upserted.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Messages assigned.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Conversation build completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Conversation build failed.", StringComparison.Ordinal));

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

    private static SqliteConversationBuilderService CreateConversationBuilderService(TestLogger<SqliteConversationBuilderService>? logger = null)
    {
        return new SqliteConversationBuilderService(logger);
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
            CaseNumber = "DL-CONV-001",
            Title = "Synthetic Conversation Builder Case",
            ParentDirectoryPath = parentDirectoryPath,
            CorrelationId = "conversation-builder-case-create"
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

    private static async Task<int> CountRowsAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<List<StoredConversation>> LoadConversationsAsync(SqliteConnection connection)
    {
        var results = new List<StoredConversation>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                title,
                platform,
                normalized_participant_key,
                source_thread_keys_json,
                start_time_utc,
                end_time_utc,
                message_count,
                source_count,
                reconciliation_status,
                review_status
            FROM conversations
            ORDER BY id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new StoredConversation(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? "[]" : reader.GetString(4),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
                reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetString(9),
                reader.GetString(10)));
        }

        return results;
    }

    private static async Task<List<StoredMessage>> LoadMessagesAsync(SqliteConnection connection)
    {
        var results = new List<StoredMessage>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                source_import_id,
                provider_message_id,
                source_thread_id,
                conversation_id
            FROM messages
            ORDER BY provider_message_id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new StoredMessage(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return results;
    }

    private static async Task<List<StoredConversationParticipant>> LoadConversationParticipantsAsync(SqliteConnection connection, string conversationId)
    {
        var results = new List<StoredConversationParticipant>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT conversation_id, identity_id, person_id, source_import_id
            FROM conversation_participants
            WHERE conversation_id = $conversationId
            ORDER BY identity_id ASC;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new StoredConversationParticipant(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return results;
    }

    private static async Task<List<StoredConversationParticipant>> LoadAllConversationParticipantsAsync(SqliteConnection connection)
    {
        var results = new List<StoredConversationParticipant>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT conversation_id, identity_id, person_id, source_import_id
            FROM conversation_participants
            ORDER BY conversation_id ASC, identity_id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new StoredConversationParticipant(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return results;
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

    private sealed record StoredConversation(
        string Id,
        string Title,
        string? Platform,
        string? NormalizedParticipantKey,
        string SourceThreadKeysJson,
        DateTimeOffset? StartTimeUtc,
        DateTimeOffset? EndTimeUtc,
        int MessageCount,
        int SourceCount,
        string ReconciliationStatus,
        string ReviewStatus);

    private sealed record StoredMessage(
        string SourceImportId,
        string? ProviderMessageId,
        string? SourceThreadId,
        string? ConversationId);

    private sealed record StoredConversationParticipant(
        string ConversationId,
        string IdentityId,
        string? PersonId,
        string? SourceImportId);

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
