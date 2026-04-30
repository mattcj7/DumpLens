namespace DumpLens.Application.Sources;

public sealed record SourceWarningSummary
{
    public int TotalWarnings { get; init; }

    public IReadOnlyList<SourceWarningCodeCount> WarningCodeCounts { get; init; } = Array.Empty<SourceWarningCodeCount>();
}
