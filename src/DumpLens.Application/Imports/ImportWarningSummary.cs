namespace DumpLens.Application.Imports;

public sealed record ImportWarningSummary
{
    public required string WarningCode { get; init; }

    public required string Message { get; init; }

    public required int Count { get; init; }
}
