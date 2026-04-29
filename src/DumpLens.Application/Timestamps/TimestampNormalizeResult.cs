namespace DumpLens.Application.Timestamps;

public sealed record TimestampNormalizeResult
{
    public string? OriginalValue { get; init; }

    public string? TimezoneAssumption { get; init; }

    public string? ResolvedTimezoneId { get; init; }

    public DateTimeOffset? NormalizedUtc { get; init; }

    public DateTime? LocalDateTime { get; init; }

    public required string Confidence { get; init; }

    public required IReadOnlyList<TimestampNormalizeWarning> Warnings { get; init; }
}
