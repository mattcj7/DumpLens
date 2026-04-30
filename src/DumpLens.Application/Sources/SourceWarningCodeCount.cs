namespace DumpLens.Application.Sources;

public sealed record SourceWarningCodeCount
{
    public required string WarningCode { get; init; }

    public int Count { get; init; }
}
