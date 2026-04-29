namespace DumpLens.Application.Imports;

public sealed record ImportPreviewRequest
{
    public string FilePath { get; init; } = string.Empty;

    public string? WorksheetName { get; init; }

    public int RowCount { get; init; } = 50;

    public string? CorrelationId { get; init; }
}
