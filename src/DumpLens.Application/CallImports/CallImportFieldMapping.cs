namespace DumpLens.Application.CallImports;

public sealed record CallImportFieldMapping
{
    public required string DumpLensFieldName { get; init; }

    public string? SourceColumnName { get; init; }

    public int? SourceColumnOrdinal { get; init; }
}
