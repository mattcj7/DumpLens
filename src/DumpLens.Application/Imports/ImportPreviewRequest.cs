namespace DumpLens.Application.Imports;

public sealed record ImportPreviewRequest
{
    public string FilePath { get; init; } = string.Empty;

    public int RowCount { get; init; } = 50;

    public string? CorrelationId { get; init; }
}
