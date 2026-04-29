namespace DumpLens.Application.Imports;

public sealed record ImportFieldMappingSuggestion
{
    public string DumpLensFieldName { get; init; } = string.Empty;

    public string? SourceColumnName { get; init; }

    public IReadOnlyList<string> CandidateSourceColumnNames { get; init; } = Array.Empty<string>();

    public bool IsAmbiguous { get; init; }
}
