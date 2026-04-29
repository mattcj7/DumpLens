using DumpLens.Application.Imports;

namespace DumpLens.Ingestion.Tabular;

internal sealed class ImportFieldMappingSuggester
{
    private static readonly IReadOnlyDictionary<string, string[]> Aliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [ImportFieldNames.Timestamp] = ["timestamp", "datetime", "date", "time", "sent_at", "created_at"],
        [ImportFieldNames.Sender] = ["sender", "from", "author", "source", "account_from"],
        [ImportFieldNames.Recipient] = ["recipient", "to", "destination", "account_to"],
        [ImportFieldNames.MessageBody] = ["message_body", "body", "text", "message", "content"],
        [ImportFieldNames.Platform] = ["platform", "app", "service", "source_app"],
        [ImportFieldNames.Direction] = ["direction", "incoming", "outgoing"],
        [ImportFieldNames.ThreadId] = ["thread_id", "conversation_id", "chat_id", "thread", "room"],
        [ImportFieldNames.MessageId] = ["message_id", "id", "guid"],
        [ImportFieldNames.Attachment] = ["attachment", "media", "filename"],
        [ImportFieldNames.Caller] = ["caller", "from_number", "originating_number"],
        [ImportFieldNames.Callee] = ["callee", "to_number", "destination_number"],
        [ImportFieldNames.Duration] = ["duration", "duration_seconds", "call_duration"],
        [ImportFieldNames.CallType] = ["call_type", "type"]
    };

    public IReadOnlyList<ImportFieldMappingSuggestion> Suggest(IReadOnlyList<ImportPreviewColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var suggestions = new List<ImportFieldMappingSuggestion>(ImportFieldNames.All.Count);

        foreach (var fieldName in ImportFieldNames.All)
        {
            var scoredCandidates = columns
                .Select(column => new
                {
                    ColumnName = column.SourceColumnName,
                    Score = Score(fieldName, column.SourceColumnName)
                })
                .Where(static candidate => candidate.Score > 0)
                .ToArray();

            if (scoredCandidates.Length == 0)
            {
                suggestions.Add(new ImportFieldMappingSuggestion
                {
                    DumpLensFieldName = fieldName
                });
                continue;
            }

            var bestScore = scoredCandidates.Max(static candidate => candidate.Score);
            var bestCandidates = scoredCandidates
                .Where(candidate => candidate.Score == bestScore)
                .Select(candidate => candidate.ColumnName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static candidate => candidate, StringComparer.Ordinal)
                .ToArray();

            suggestions.Add(new ImportFieldMappingSuggestion
            {
                DumpLensFieldName = fieldName,
                SourceColumnName = bestCandidates.Length == 1 ? bestCandidates[0] : null,
                CandidateSourceColumnNames = bestCandidates,
                IsAmbiguous = bestCandidates.Length > 1
            });
        }

        return suggestions;
    }

    public int CountKnownHeaderMatches(IReadOnlyList<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values.Count(value => !string.IsNullOrWhiteSpace(value) && MatchesAnyAlias(value!));
    }

    private static int Score(string fieldName, string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return 0;
        }

        var normalizedColumnName = Normalize(columnName);
        if (normalizedColumnName.Length == 0)
        {
            return 0;
        }

        var columnTokens = Tokenize(columnName);
        var bestScore = 0;

        foreach (var alias in Aliases[fieldName])
        {
            var normalizedAlias = Normalize(alias);
            if (normalizedAlias == normalizedColumnName)
            {
                bestScore = Math.Max(bestScore, normalizedAlias.Length <= 2 ? 75 : 100);
                continue;
            }

            var aliasTokens = Tokenize(alias);
            if (aliasTokens.Count == 0)
            {
                continue;
            }

            if (aliasTokens.SetEquals(columnTokens))
            {
                bestScore = Math.Max(bestScore, 90);
                continue;
            }

            if (aliasTokens.Count == 1 && aliasTokens.First().Length <= 4)
            {
                continue;
            }

            if (aliasTokens.IsSubsetOf(columnTokens))
            {
                bestScore = Math.Max(bestScore, 80);
            }
        }

        return bestScore;
    }

    private static bool MatchesAnyAlias(string value)
    {
        return Aliases.Keys.Any(fieldName => Score(fieldName, value) > 0);
    }

    private static string Normalize(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    private static HashSet<string> Tokenize(string value)
    {
        return value
            .Split([' ', '_', '-', '.', '/', '\\', '(', ')', '[', ']', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => part.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }
}
