using System.Globalization;
using System.Text.RegularExpressions;
using DumpLens.Application.Search;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpLens.Persistence.Search;

public sealed class SqliteMessageSearchIndexService : IMessageSearchIndexService
{
    private const int DefaultMaxResults = 100;
    private const int MaxResultsLimit = 500;
    private const string RebuildOperationName = "message_search_index_rebuild";
    private const string SearchOperationName = "message_search";

    private static readonly Regex QuerySegmentPattern = new(
        "\"([^\"]+)\"|([^\\s\"]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SearchTokenPattern = new(
        "[\\p{L}\\p{N}_]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ILogger<SqliteMessageSearchIndexService> _logger;

    public SqliteMessageSearchIndexService(ILogger<SqliteMessageSearchIndexService>? logger = null)
    {
        _logger = logger ?? NullLogger<SqliteMessageSearchIndexService>.Instance;
    }

    public async Task<RebuildMessageSearchIndexResult> RebuildAsync(
        RebuildMessageSearchIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = ValidateAndNormalize(request);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var failureStage = "validation";

        _logger.LogInformation(
            "Message search index rebuild started. operation={Operation} correlation_id={CorrelationId} case_id={CaseId}",
            RebuildOperationName,
            normalizedRequest.CorrelationId,
            normalizedRequest.CaseId);

        try
        {
            await using var connection = new SqliteConnection(BuildConnectionString(normalizedRequest.CaseDatabasePath));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            failureStage = "case_validation";
            await EnsureCaseExistsAsync(connection, transaction, normalizedRequest.CaseId, cancellationToken).ConfigureAwait(false);

            failureStage = "case_index_clear";
            await DeleteCaseIndexAsync(connection, transaction, normalizedRequest.CaseId, cancellationToken).ConfigureAwait(false);

            failureStage = "case_index_insert";
            await InsertCaseIndexAsync(connection, transaction, normalizedRequest.CaseId, cancellationToken).ConfigureAwait(false);

            failureStage = "case_index_count";
            var indexedCount = await CountIndexedRowsAsync(connection, transaction, normalizedRequest.CaseId, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            var completedAtUtc = DateTimeOffset.UtcNow;
            var durationMs = GetDurationMilliseconds(startedAtUtc, completedAtUtc);

            _logger.LogInformation(
                "Message search index rebuild completed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} indexed_count={IndexedCount} duration_ms={DurationMs}",
                RebuildOperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                indexedCount,
                durationMs);

            return new RebuildMessageSearchIndexResult
            {
                CaseId = normalizedRequest.CaseId,
                IndexedCount = indexedCount,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Message search index rebuild failed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} failure_stage={FailureStage} failure_type={FailureType}",
                RebuildOperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                failureStage,
                exception.GetType().Name);
            throw;
        }
    }

    public async Task<SearchMessagesResult> SearchAsync(
        SearchMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = ValidateAndNormalize(request);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var failureStage = "validation";

        _logger.LogInformation(
            "Message search requested. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} max_results={MaxResults}",
            SearchOperationName,
            normalizedRequest.CorrelationId,
            normalizedRequest.CaseId,
            normalizedRequest.MaxResults);

        try
        {
            if (!TryBuildMatchQuery(
                    normalizedRequest.QueryText,
                    out var matchQuery,
                    out var validationErrorCode,
                    out var validationMessage))
            {
                var completedAtUtc = DateTimeOffset.UtcNow;
                var durationMs = GetDurationMilliseconds(startedAtUtc, completedAtUtc);

                _logger.LogInformation(
                    "Message search completed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} result_count={ResultCount} is_query_valid={IsQueryValid} duration_ms={DurationMs}",
                    SearchOperationName,
                    normalizedRequest.CorrelationId,
                    normalizedRequest.CaseId,
                    0,
                    false,
                    durationMs);

                return new SearchMessagesResult
                {
                    CaseId = normalizedRequest.CaseId,
                    IsQueryValid = false,
                    ValidationErrorCode = validationErrorCode,
                    ValidationMessage = validationMessage,
                    ResultCount = 0,
                    StartedAtUtc = startedAtUtc,
                    CompletedAtUtc = completedAtUtc
                };
            }

            await using var connection = new SqliteConnection(BuildConnectionString(normalizedRequest.CaseDatabasePath));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

            failureStage = "case_validation";
            await EnsureCaseExistsAsync(connection, transaction: null, normalizedRequest.CaseId, cancellationToken).ConfigureAwait(false);

            failureStage = "search_execute";
            IReadOnlyList<MessageSearchResult> results;
            try
            {
                results = await ExecuteSearchAsync(
                        connection,
                        normalizedRequest.CaseId,
                        matchQuery!,
                        normalizedRequest.MaxResults,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SqliteException exception)
            {
                _logger.LogError(
                    exception,
                    "Message search failed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} failure_stage={FailureStage} failure_type={FailureType}",
                    SearchOperationName,
                    normalizedRequest.CorrelationId,
                    normalizedRequest.CaseId,
                    failureStage,
                    exception.GetType().Name);

                var completedAtUtc = DateTimeOffset.UtcNow;
                return new SearchMessagesResult
                {
                    CaseId = normalizedRequest.CaseId,
                    IsQueryValid = false,
                    ValidationErrorCode = MessageSearchValidationCodes.UnsupportedQuery,
                    ValidationMessage = "The search query could not be processed safely.",
                    ResultCount = 0,
                    StartedAtUtc = startedAtUtc,
                    CompletedAtUtc = completedAtUtc
                };
            }

            var finishedAtUtc = DateTimeOffset.UtcNow;
            var durationMsCompleted = GetDurationMilliseconds(startedAtUtc, finishedAtUtc);

            _logger.LogInformation(
                "Message search completed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} result_count={ResultCount} is_query_valid={IsQueryValid} duration_ms={DurationMs}",
                SearchOperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                results.Count,
                true,
                durationMsCompleted);

            return new SearchMessagesResult
            {
                CaseId = normalizedRequest.CaseId,
                IsQueryValid = true,
                ResultCount = results.Count,
                Results = results,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = finishedAtUtc
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Message search failed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} failure_stage={FailureStage} failure_type={FailureType}",
                SearchOperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                failureStage,
                exception.GetType().Name);
            throw;
        }
    }

    private static async Task DeleteCaseIndexAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string caseId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM message_search_index
            WHERE case_id = $caseId;
            """;
        command.Parameters.AddWithValue("$caseId", caseId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCaseIndexAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string caseId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO message_search_index (
                case_id,
                message_id,
                conversation_id,
                source_import_id,
                source_artifact_id,
                provider_message_id,
                source_thread_id,
                event_time_utc,
                direction,
                platform,
                deleted_status,
                message_body
            )
            SELECT
                messages.case_id,
                messages.id,
                COALESCE(messages.conversation_id, ''),
                messages.source_import_id,
                COALESCE(messages.source_artifact_id, ''),
                COALESCE(messages.provider_message_id, ''),
                COALESCE(messages.source_thread_id, ''),
                COALESCE(messages.event_time_utc, ''),
                COALESCE(messages.direction, ''),
                COALESCE(messages.platform, ''),
                COALESCE(messages.deleted_status, 'present'),
                COALESCE(messages.message_body, '')
            FROM messages
            WHERE messages.case_id = $caseId;
            """;
        command.Parameters.AddWithValue("$caseId", caseId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> CountIndexedRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string caseId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM message_search_index
            WHERE case_id = $caseId;
            """;
        command.Parameters.AddWithValue("$caseId", caseId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<MessageSearchResult>> ExecuteSearchAsync(
        SqliteConnection connection,
        string caseId,
        string matchQuery,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var results = new List<MessageSearchResult>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                case_id,
                message_id,
                conversation_id,
                source_import_id,
                source_artifact_id,
                provider_message_id,
                source_thread_id,
                event_time_utc,
                direction,
                platform,
                deleted_status,
                snippet(message_search_index, 11, '[[', ']]', ' ... ', 12) AS snippet_text,
                bm25(message_search_index) AS rank
            FROM message_search_index
            WHERE case_id = $caseId
              AND message_search_index MATCH $matchQuery
            ORDER BY rank ASC, event_time_utc DESC, message_id ASC
            LIMIT $maxResults;
            """;
        command.Parameters.AddWithValue("$caseId", caseId);
        command.Parameters.AddWithValue("$matchQuery", matchQuery);
        command.Parameters.AddWithValue("$maxResults", maxResults);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var conversationId = NormalizeOptional(reader.IsDBNull(2) ? null : reader.GetString(2));
            var providerMessageId = NormalizeOptional(reader.IsDBNull(5) ? null : reader.GetString(5));
            var sourceThreadId = NormalizeOptional(reader.IsDBNull(6) ? null : reader.GetString(6));
            var eventTimeUtc = ParseNullableDateTimeOffset(reader.IsDBNull(7) ? null : reader.GetString(7));
            var direction = NormalizeOptional(reader.IsDBNull(8) ? null : reader.GetString(8));
            var platform = NormalizeOptional(reader.IsDBNull(9) ? null : reader.GetString(9));
            var snippet = NormalizeOptional(reader.IsDBNull(11) ? null : reader.GetString(11));
            var rank = reader.IsDBNull(12) ? (double?)null : reader.GetDouble(12);

            results.Add(new MessageSearchResult
            {
                CaseId = reader.GetString(0),
                MessageId = reader.GetString(1),
                ConversationId = conversationId,
                SourceImportId = reader.GetString(3),
                SourceArtifactId = reader.GetString(4),
                ProviderMessageId = providerMessageId,
                SourceThreadId = sourceThreadId,
                EventTimeUtc = eventTimeUtc,
                Direction = direction,
                Platform = platform,
                DeletedStatus = reader.GetString(10),
                Snippet = snippet,
                Rank = rank
            });
        }

        return results;
    }

    private static async Task EnsureCaseExistsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string caseId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT 1
            FROM cases
            WHERE id = $caseId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$caseId", caseId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            throw new InvalidOperationException("The requested case_id was not found in the case database.");
        }
    }

    private static NormalizedRebuildRequest ValidateAndNormalize(RebuildMessageSearchIndexRequest request)
    {
        var caseId = NormalizeRequired(request.CaseId, nameof(request.CaseId));
        var caseDatabasePath = NormalizeAbsoluteFilePath(request.CaseDatabasePath, nameof(request.CaseDatabasePath));
        if (!File.Exists(caseDatabasePath) || Directory.Exists(caseDatabasePath))
        {
            throw new FileNotFoundException("The case database path must exist and point to a file.", caseDatabasePath);
        }

        return new NormalizedRebuildRequest(
            CaseId: caseId,
            CaseDatabasePath: caseDatabasePath,
            CorrelationId: NormalizeCorrelationId(request.CorrelationId));
    }

    private static NormalizedSearchRequest ValidateAndNormalize(SearchMessagesRequest request)
    {
        var caseId = NormalizeRequired(request.CaseId, nameof(request.CaseId));
        var caseDatabasePath = NormalizeAbsoluteFilePath(request.CaseDatabasePath, nameof(request.CaseDatabasePath));
        if (!File.Exists(caseDatabasePath) || Directory.Exists(caseDatabasePath))
        {
            throw new FileNotFoundException("The case database path must exist and point to a file.", caseDatabasePath);
        }

        return new NormalizedSearchRequest(
            CaseId: caseId,
            CaseDatabasePath: caseDatabasePath,
            QueryText: request.QueryText ?? string.Empty,
            MaxResults: NormalizeMaxResults(request.MaxResults),
            CorrelationId: NormalizeCorrelationId(request.CorrelationId));
    }

    private static bool TryBuildMatchQuery(
        string rawQueryText,
        out string? matchQuery,
        out string validationErrorCode,
        out string validationMessage)
    {
        matchQuery = null;
        validationErrorCode = MessageSearchValidationCodes.EmptyQuery;
        validationMessage = "Enter one or more search terms.";

        if (string.IsNullOrWhiteSpace(rawQueryText))
        {
            return false;
        }

        var phrases = new List<string>();
        foreach (Match match in QuerySegmentPattern.Matches(rawQueryText))
        {
            var segment = match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Groups[2].Value;

            var normalizedSegment = NormalizeSearchSegment(segment);
            if (normalizedSegment is not null)
            {
                phrases.Add(normalizedSegment);
            }
        }

        if (phrases.Count == 0)
        {
            validationErrorCode = MessageSearchValidationCodes.UnsupportedQuery;
            validationMessage = "The search query did not contain any searchable terms.";
            return false;
        }

        matchQuery = string.Join(
            " AND ",
            phrases.Select(static phrase => $"\"{EscapeFtsPhrase(phrase)}\""));
        return true;
    }

    private static string? NormalizeSearchSegment(string segment)
    {
        var tokens = SearchTokenPattern.Matches(segment)
            .Select(static match => match.Value)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        return tokens.Length == 0
            ? null
            : string.Join(" ", tokens);
    }

    private static string EscapeFtsPhrase(string value)
    {
        return value.Replace("\"", "\"\"", StringComparison.Ordinal);
    }

    private static int NormalizeMaxResults(int? maxResults)
    {
        if (!maxResults.HasValue)
        {
            return DefaultMaxResults;
        }

        return Math.Clamp(maxResults.Value, 1, MaxResultsLimit);
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

    private static DateTimeOffset? ParseNullableDateTimeOffset(string? value)
    {
        var normalizedValue = NormalizeOptional(value);
        return normalizedValue is null
            ? null
            : DateTimeOffset.Parse(normalizedValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
    }

    private static long GetDurationMilliseconds(DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc)
    {
        return (long)Math.Max(0, (completedAtUtc - startedAtUtc).TotalMilliseconds);
    }

    private sealed record NormalizedRebuildRequest(
        string CaseId,
        string CaseDatabasePath,
        string CorrelationId);

    private sealed record NormalizedSearchRequest(
        string CaseId,
        string CaseDatabasePath,
        string QueryText,
        int MaxResults,
        string CorrelationId);
}
