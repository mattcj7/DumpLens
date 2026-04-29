namespace DumpLens.Application.Timestamps;

public sealed record TimestampNormalizeRequest
{
    public string? OriginalValue { get; init; }

    public string? TimezoneAssumption { get; init; }
}
