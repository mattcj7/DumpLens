namespace DumpLens.Application.Imports;

public sealed record ImportPreviewRow
{
    public int RowNumber { get; init; }

    public IReadOnlyList<string?> Values { get; init; } = Array.Empty<string?>();
}
