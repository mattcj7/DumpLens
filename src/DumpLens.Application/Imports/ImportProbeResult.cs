namespace DumpLens.Application.Imports;

public sealed record ImportProbeResult
{
    public string CorrelationId { get; init; } = string.Empty;

    public ImportSourceKind SourceKind { get; init; }

    public string FilePath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string FileExtension { get; init; } = string.Empty;

    public bool IsSupported { get; init; }

    public bool IsTabular { get; init; }

    public char? DetectedDelimiter { get; init; }

    public IReadOnlyList<string> WorksheetNames { get; init; } = Array.Empty<string>();

    public string? SelectedWorksheetName { get; init; }

    public bool HasHeaderRow { get; init; }

    public int RequestedPreviewRowCount { get; init; }

    public int ReturnedPreviewRowCount { get; init; }

    public IReadOnlyList<ImportPreviewColumn> Columns { get; init; } = Array.Empty<ImportPreviewColumn>();

    public IReadOnlyList<ImportPreviewRow> PreviewRows { get; init; } = Array.Empty<ImportPreviewRow>();

    public IReadOnlyList<ImportFieldMappingSuggestion> FieldMappingSuggestions { get; init; } = Array.Empty<ImportFieldMappingSuggestion>();

    public IReadOnlyList<ImportWarning> Warnings { get; init; } = Array.Empty<ImportWarning>();
}
