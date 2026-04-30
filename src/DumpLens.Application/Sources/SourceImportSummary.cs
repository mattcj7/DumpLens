namespace DumpLens.Application.Sources;

public sealed record SourceImportSummary
{
    public required string SourceImportId { get; init; }

    public required string SourceName { get; init; }

    public required string SourceType { get; init; }

    public string? Platform { get; init; }

    public required string ImportStatus { get; init; }

    public int RecordCount { get; init; }

    public int WarningCount { get; init; }

    public required DateTimeOffset ImportedAtUtc { get; init; }

    public required string OriginalFilename { get; init; }

    public long? FileSizeBytes { get; init; }

    public required string FileSha256 { get; init; }
}
