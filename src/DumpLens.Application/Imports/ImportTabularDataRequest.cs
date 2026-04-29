namespace DumpLens.Application.Imports;

public sealed record ImportTabularDataRequest
{
    public string FilePath { get; init; } = string.Empty;

    public string? WorksheetName { get; init; }

    public int? RowLimit { get; init; }

    public string? CorrelationId { get; init; }
}
