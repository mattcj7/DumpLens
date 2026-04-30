namespace DumpLens.Application.Sources;

public sealed record SourceImportDetail
{
    public required string SourceImportId { get; init; }

    public required string SourceName { get; init; }

    public required string SourceType { get; init; }

    public string? Platform { get; init; }

    public required string OriginalFilename { get; init; }

    public string? StoredFilePath { get; init; }

    public long? FileSizeBytes { get; init; }

    public required string FileSha256 { get; init; }

    public required DateTimeOffset ImportedAtUtc { get; init; }

    public string? ImportedByUserId { get; init; }

    public required string ImportStatus { get; init; }

    public int RecordCount { get; init; }

    public int WarningCount { get; init; }

    public bool HasNotes { get; init; }

    public bool HasSourceMetadata { get; init; }

    public required SourceWarningSummary WarningSummary { get; init; }
}
