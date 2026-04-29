using DumpLens.Application.Imports;
using DumpLens.Ingestion.Tabular;

namespace DumpLens.Ingestion.Csv;

public sealed class CsvFieldMappingSuggester
{
    private readonly ImportFieldMappingSuggester _inner = new();

    public IReadOnlyList<ImportFieldMappingSuggestion> Suggest(IReadOnlyList<ImportPreviewColumn> columns)
    {
        return _inner.Suggest(columns);
    }

    internal int CountKnownHeaderMatches(IReadOnlyList<string?> values)
    {
        return _inner.CountKnownHeaderMatches(values);
    }
}
