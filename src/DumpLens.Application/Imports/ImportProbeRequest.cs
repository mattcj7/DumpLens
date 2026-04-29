namespace DumpLens.Application.Imports;

public sealed record ImportProbeRequest
{
    public string FilePath { get; init; } = string.Empty;

    public int PreviewRowCount { get; init; } = 50;

    public string? CorrelationId { get; init; }
}
