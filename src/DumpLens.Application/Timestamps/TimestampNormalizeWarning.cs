namespace DumpLens.Application.Timestamps;

public sealed record TimestampNormalizeWarning
{
    public required string Code { get; init; }

    public required string Message { get; init; }
}
