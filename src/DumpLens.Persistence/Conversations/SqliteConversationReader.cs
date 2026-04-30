using System.Globalization;
using DumpLens.Application.Conversations;
using Microsoft.Data.Sqlite;

namespace DumpLens.Persistence.Conversations;

public sealed class SqliteConversationReader : IConversationReader
{
    public async Task<IReadOnlyList<ConversationSummary>> GetSummariesAsync(
        LoadConversationSummariesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = Normalize(request);
        var results = new List<ConversationSummary>();

        await using var connection = new SqliteConnection(BuildConnectionString(normalizedRequest.CaseDatabasePath));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                title,
                platform,
                start_time_utc,
                end_time_utc,
                message_count,
                source_count,
                gap_count,
                priority_score,
                reconciliation_status,
                review_status
            FROM conversations
            WHERE case_id = $caseId
            ORDER BY
                CASE WHEN end_time_utc IS NULL THEN 1 ELSE 0 END ASC,
                end_time_utc DESC,
                title COLLATE NOCASE ASC,
                id ASC;
            """;
        command.Parameters.AddWithValue("$caseId", normalizedRequest.CaseId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ConversationSummary
            {
                ConversationId = reader.GetString(0),
                Title = reader.GetString(1),
                Platform = reader.IsDBNull(2) ? null : reader.GetString(2),
                StartTimeUtc = ParseNullableUtc(reader, 3),
                EndTimeUtc = ParseNullableUtc(reader, 4),
                MessageCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                SourceCount = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                GapCount = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                PriorityScore = reader.IsDBNull(8) ? 0 : reader.GetDouble(8),
                ReconciliationStatus = reader.GetString(9),
                ReviewStatus = reader.GetString(10)
            });
        }

        return results;
    }

    public async Task<ConversationThread?> GetThreadAsync(
        LoadConversationThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = Normalize(request);

        await using var connection = new SqliteConnection(BuildConnectionString(normalizedRequest.CaseDatabasePath));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

        if (!await ConversationExistsAsync(connection, normalizedRequest, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var messageRows = new List<MessageRow>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT
                    m.id,
                    m.event_time_utc,
                    m.created_at_utc,
                    m.direction,
                    COALESCE(
                        NULLIF(TRIM(sender.display_value), ''),
                        NULLIF(TRIM(sender.normalized_value), ''),
                        'Unknown sender') AS sender_label,
                    m.message_body,
                    COALESCE(
                        NULLIF(TRIM(m.platform), ''),
                        NULLIF(TRIM(si.platform), '')) AS platform,
                    m.deleted_status,
                    m.source_import_id,
                    si.source_name,
                    si.source_type,
                    si.platform,
                    si.original_filename,
                    m.source_artifact_id,
                    sa.artifact_locator,
                    m.provider_message_id,
                    m.source_thread_id,
                    m.message_body_sha256
                FROM messages m
                LEFT JOIN identities sender ON sender.id = m.sender_identity_id
                LEFT JOIN source_imports si ON si.id = m.source_import_id
                LEFT JOIN source_artifacts sa ON sa.id = m.source_artifact_id
                WHERE m.case_id = $caseId
                  AND m.conversation_id = $conversationId
                ORDER BY
                    CASE WHEN m.event_time_utc IS NULL THEN 1 ELSE 0 END ASC,
                    m.event_time_utc ASC,
                    m.created_at_utc ASC,
                    m.id ASC;
                """;
            command.Parameters.AddWithValue("$caseId", normalizedRequest.CaseId);
            command.Parameters.AddWithValue("$conversationId", normalizedRequest.ConversationId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var sourceImportId = reader.GetString(8);
                var sourceName = reader.IsDBNull(9) ? "-" : reader.GetString(9);
                var sourceType = reader.IsDBNull(10) ? "-" : reader.GetString(10);
                var originalFilename = reader.IsDBNull(12) ? "-" : reader.GetString(12);
                var sourceArtifactId = reader.IsDBNull(13) ? null : reader.GetString(13);
                var sourceContext = new ConversationSourceContext
                {
                    SourceImportId = sourceImportId,
                    SourceName = sourceName,
                    SourceType = sourceType,
                    Platform = reader.IsDBNull(11) ? null : reader.GetString(11),
                    OriginalFilename = originalFilename,
                    SourceArtifactId = sourceArtifactId,
                    ArtifactLocator = reader.IsDBNull(14) ? null : reader.GetString(14),
                    ProviderMessageId = reader.IsDBNull(15) ? null : reader.GetString(15),
                    SourceThreadId = reader.IsDBNull(16) ? null : reader.GetString(16),
                    MessageHashPrefix = BuildHashPrefix(reader.IsDBNull(17) ? null : reader.GetString(17))
                };

                messageRows.Add(new MessageRow(
                    reader.GetString(0),
                    ParseNullableUtc(reader, 1),
                    ParseUtc(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? "present" : reader.GetString(7),
                    HasSourceReference: true,
                    sourceContext));
            }
        }

        var recipientsByMessageId = await LoadRecipientLabelsAsync(
                connection,
                normalizedRequest.CaseId,
                normalizedRequest.ConversationId,
                cancellationToken)
            .ConfigureAwait(false);

        return new ConversationThread
        {
            ConversationId = normalizedRequest.ConversationId,
            Messages = messageRows
                .Select(message => new ConversationThreadMessage
                {
                    MessageId = message.MessageId,
                    EventTimeUtc = message.EventTimeUtc,
                    CreatedAtUtc = message.CreatedAtUtc,
                    Direction = message.Direction,
                    SenderDisplayLabel = message.SenderDisplayLabel,
                    RecipientDisplayLabels = recipientsByMessageId.TryGetValue(message.MessageId, out var recipients)
                        ? recipients
                        : [],
                    MessageBody = message.MessageBody,
                    Platform = message.Platform,
                    DeletedStatus = message.DeletedStatus,
                    HasSourceReference = message.HasSourceReference,
                    SourceContext = message.SourceContext
                })
                .ToArray()
        };
    }

    private static async Task<bool> ConversationExistsAsync(
        SqliteConnection connection,
        NormalizedConversationThreadRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT 1
            FROM conversations
            WHERE case_id = $caseId
              AND id = $conversationId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$caseId", request.CaseId);
        command.Parameters.AddWithValue("$conversationId", request.ConversationId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    private static async Task<Dictionary<string, IReadOnlyList<string>>> LoadRecipientLabelsAsync(
        SqliteConnection connection,
        string caseId,
        string conversationId,
        CancellationToken cancellationToken)
    {
        var recipientsByMessageId = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                mr.message_id,
                COALESCE(
                    NULLIF(TRIM(recipient.display_value), ''),
                    NULLIF(TRIM(recipient.normalized_value), ''),
                    'Unknown recipient') AS recipient_label
            FROM message_recipients mr
            INNER JOIN messages m ON m.id = mr.message_id
            LEFT JOIN identities recipient ON recipient.id = mr.recipient_identity_id
            WHERE mr.case_id = $caseId
              AND m.conversation_id = $conversationId
            ORDER BY mr.message_id ASC, mr.created_at_utc ASC, mr.id ASC;
            """;
        command.Parameters.AddWithValue("$caseId", caseId);
        command.Parameters.AddWithValue("$conversationId", conversationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var messageId = reader.GetString(0);
            if (!recipientsByMessageId.TryGetValue(messageId, out var recipients))
            {
                recipients = [];
                recipientsByMessageId[messageId] = recipients;
            }

            recipients.Add(reader.GetString(1));
        }

        return recipientsByMessageId.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.Ordinal);
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

    private static DateTimeOffset ParseUtc(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ToUniversalTime();
    }

    private static DateTimeOffset? ParseNullableUtc(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : ParseUtc(reader.GetString(ordinal));
    }

    private static string BuildHashPrefix(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return "-";
        }

        var trimmed = hash.Trim();
        return trimmed.Length <= 12
            ? trimmed
            : trimmed[..12];
    }

    private static NormalizedConversationSummariesRequest Normalize(LoadConversationSummariesRequest request)
    {
        return new NormalizedConversationSummariesRequest(
            NormalizeRequired(request.CaseId, nameof(request.CaseId)),
            NormalizeAbsoluteFilePath(request.CaseDatabasePath, nameof(request.CaseDatabasePath)));
    }

    private static NormalizedConversationThreadRequest Normalize(LoadConversationThreadRequest request)
    {
        return new NormalizedConversationThreadRequest(
            NormalizeRequired(request.CaseId, nameof(request.CaseId)),
            NormalizeAbsoluteFilePath(request.CaseDatabasePath, nameof(request.CaseDatabasePath)),
            NormalizeRequired(request.ConversationId, nameof(request.ConversationId)));
    }

    private static string NormalizeAbsoluteFilePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        if (!Path.IsPathRooted(path))
        {
            throw new ArgumentException("The path must be absolute.", parameterName);
        }

        var fullPath = Path.GetFullPath(path.Trim());
        if (!File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("The case database path must exist and point to a file.", fullPath);
        }

        return fullPath;
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }

    private sealed record NormalizedConversationSummariesRequest(
        string CaseId,
        string CaseDatabasePath);

    private sealed record NormalizedConversationThreadRequest(
        string CaseId,
        string CaseDatabasePath,
        string ConversationId);

    private sealed record MessageRow(
        string MessageId,
        DateTimeOffset? EventTimeUtc,
        DateTimeOffset CreatedAtUtc,
        string? Direction,
        string SenderDisplayLabel,
        string? MessageBody,
        string? Platform,
        string DeletedStatus,
        bool HasSourceReference,
        ConversationSourceContext SourceContext);
}
