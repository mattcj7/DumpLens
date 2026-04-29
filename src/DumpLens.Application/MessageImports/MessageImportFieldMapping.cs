namespace DumpLens.Application.MessageImports;

public sealed record MessageImportFieldMapping
{
    public required string DumpLensFieldName { get; init; }

    public string? SourceColumnName { get; init; }

    public int? SourceColumnOrdinal { get; init; }
}
