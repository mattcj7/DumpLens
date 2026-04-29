namespace DumpLens.Application.Imports;

public sealed record ImportPreviewColumn
{
    public int Ordinal { get; init; }

    public string SourceColumnName { get; init; } = string.Empty;

    public bool IsGenerated { get; init; }
}
