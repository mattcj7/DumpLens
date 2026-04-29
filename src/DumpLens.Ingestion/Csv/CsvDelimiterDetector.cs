namespace DumpLens.Ingestion.Csv;

public sealed class CsvDelimiterDetector
{
    private static readonly char[] SupportedDelimiters = [',', '\t', ';', '|'];

    public CsvDelimiterDetectionResult Detect(string filePath, int maxSampleRecords = 20)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (maxSampleRecords <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSampleRecords), "The sample record count must be positive.");
        }

        CsvDelimiterCandidateResult? bestCandidate = null;
        List<string[]>? fallbackCsvRecords = null;

        foreach (var delimiter in SupportedDelimiters)
        {
            var candidate = AnalyzeCandidate(filePath, delimiter, maxSampleRecords);
            if (delimiter == ',')
            {
                fallbackCsvRecords = candidate.Records;
            }

            if (bestCandidate is null || candidate.Score > bestCandidate.Score)
            {
                bestCandidate = candidate;
            }
        }

        if (bestCandidate is null)
        {
            return new CsvDelimiterDetectionResult
            {
                IsTabular = false
            };
        }

        if (bestCandidate.IsTabular)
        {
            return new CsvDelimiterDetectionResult
            {
                Delimiter = bestCandidate.Delimiter,
                IsTabular = true,
                SampleRecords = bestCandidate.Records
            };
        }

        var extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
            && fallbackCsvRecords is { Count: > 0 })
        {
            return new CsvDelimiterDetectionResult
            {
                Delimiter = ',',
                IsTabular = true,
                SampleRecords = fallbackCsvRecords
            };
        }

        return new CsvDelimiterDetectionResult
        {
            IsTabular = false,
            SampleRecords = bestCandidate.Records
        };
    }

    private static CsvDelimiterCandidateResult AnalyzeCandidate(string filePath, char delimiter, int maxSampleRecords)
    {
        var records = new List<string[]>();

        using var reader = new CsvRecordReader(filePath, delimiter);
        while (records.Count < maxSampleRecords)
        {
            var record = reader.ReadRecord();
            if (record is null)
            {
                break;
            }

            if (IsBlankRecord(record))
            {
                continue;
            }

            records.Add(record);
        }

        if (records.Count == 0)
        {
            return new CsvDelimiterCandidateResult(delimiter, records, false, int.MinValue);
        }

        var widthGroups = records
            .GroupBy(static record => record.Length)
            .Select(static group => new { Width = group.Key, Count = group.Count() })
            .OrderByDescending(static group => group.Count)
            .ThenByDescending(static group => group.Width)
            .ToArray();

        var modalWidth = widthGroups[0].Width;
        var modalCount = widthGroups[0].Count;
        var inconsistentCount = records.Count - modalCount;
        var isTabular = modalWidth > 1;
        var score = (isTabular ? 1000 : 0) + (modalCount * 100) + (modalWidth * 10) - (inconsistentCount * 25);

        return new CsvDelimiterCandidateResult(delimiter, records, isTabular, score);
    }

    private static bool IsBlankRecord(IReadOnlyList<string?> values)
    {
        return values.All(static value => string.IsNullOrWhiteSpace(value));
    }
}

public sealed record CsvDelimiterDetectionResult
{
    public char? Delimiter { get; init; }

    public bool IsTabular { get; init; }

    public IReadOnlyList<string[]> SampleRecords { get; init; } = Array.Empty<string[]>();
}

internal sealed record CsvDelimiterCandidateResult(
    char Delimiter,
    List<string[]> Records,
    bool IsTabular,
    int Score);
