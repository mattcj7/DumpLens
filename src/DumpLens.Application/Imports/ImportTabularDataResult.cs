namespace DumpLens.Application.Imports;

public sealed record ImportTabularDataResult
{
    public string CorrelationId { get; init; } = string.Empty;

    public ImportSourceKind SourceKind { get; init; }

    public string FilePath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string FileExtension { get; init; } = string.Empty;

    public bool IsSupported { get; init; }

    public bool IsTabular { get; init; }

    public IReadOnlyList<string> WorksheetNames { get; init; } = Array.Empty<string>();

    public string? SelectedWorksheetName { get; init; }

    public bool HasHeaderRow { get; init; }

    public int ReturnedRowCount { get; init; }

    public IReadOnlyList<ImportPreviewColumn> Columns { get; init; } = Array.Empty<ImportPreviewColumn>();

    public IReadOnlyList<ImportPreviewRow> Rows { get; init; } = Array.Empty<ImportPreviewRow>();

    public IReadOnlyList<ImportWarning> Warnings { get; init; } = Array.Empty<ImportWarning>();
}
