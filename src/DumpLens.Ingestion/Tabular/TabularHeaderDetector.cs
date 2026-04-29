using System.Globalization;

namespace DumpLens.Ingestion.Tabular;

internal sealed class TabularHeaderDetector
{
    private readonly ImportFieldMappingSuggester _fieldMappingSuggester;

    public TabularHeaderDetector(ImportFieldMappingSuggester fieldMappingSuggester)
    {
        _fieldMappingSuggester = fieldMappingSuggester ?? throw new ArgumentNullException(nameof(fieldMappingSuggester));
    }

    public TabularHeaderDecision DetectHeaderRow(IReadOnlyList<TabularPreviewRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return new TabularHeaderDecision(false, false, Array.Empty<string>());
        }

        var firstRecord = records[0].Values.Select(static value => value ?? string.Empty).ToArray();
        var secondRecord = records.Count > 1
            ? records[1].Values.Select(static value => value ?? string.Empty).ToArray()
            : Array.Empty<string>();
        var nonEmptyCount = firstRecord.Count(static value => !string.IsNullOrWhiteSpace(value));
        var distinctCount = firstRecord
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var knownHeaderMatches = _fieldMappingSuggester.CountKnownHeaderMatches(firstRecord);
        var firstRowDataLikeCount = firstRecord.Count(LooksLikeDataValue);
        var secondRowDataLikeCount = secondRecord.Count(LooksLikeDataValue);
        var missingNamesCount = firstRecord.Length - nonEmptyCount;
        var duplicateNamesCount = nonEmptyCount - distinctCount;
        var headerScore =
            (knownHeaderMatches * 3)
            + (nonEmptyCount == firstRecord.Length ? 1 : 0)
            + (distinctCount == nonEmptyCount ? 1 : 0)
            - (missingNamesCount * 2)
            - duplicateNamesCount
            - firstRowDataLikeCount
            + (secondRowDataLikeCount > firstRowDataLikeCount ? 1 : 0);

        if (knownHeaderMatches >= 1 && headerScore >= 2)
        {
            return new TabularHeaderDecision(true, false, firstRecord);
        }

        var appearsHeaderLike =
            nonEmptyCount >= Math.Max(1, firstRecord.Length / 2)
            && distinctCount >= Math.Max(1, nonEmptyCount / 2)
            && firstRowDataLikeCount <= Math.Max(1, firstRecord.Length / 2)
            && secondRowDataLikeCount > firstRowDataLikeCount;

        return new TabularHeaderDecision(false, appearsHeaderLike, Array.Empty<string>());
    }

    private static bool LooksLikeDataValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmedValue = value.Trim();
        if (DateTimeOffset.TryParse(trimmedValue, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
        {
            return true;
        }

        if (Guid.TryParse(trimmedValue, out _))
        {
            return true;
        }

        if (double.TryParse(trimmedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return true;
        }

        var digitCount = trimmedValue.Count(char.IsDigit);
        if (digitCount >= 7)
        {
            return true;
        }

        return trimmedValue.Length > 40;
    }
}

internal sealed record TabularPreviewRecord(int RowNumber, IReadOnlyList<string?> Values);

internal sealed record TabularHeaderDecision(
    bool HasHeaderRow,
    bool IsAmbiguous,
    IReadOnlyList<string> HeaderValues);
