namespace DumpLens.Application.Sources;

public sealed record RegisterSourceResult
{
    public required string SourceImportId { get; init; }

    public required string CaseId { get; init; }

    public required string SourceName { get; init; }

    public required string SourceType { get; init; }

    public string? Platform { get; init; }

    public required string OriginalFilename { get; init; }

    public required string StoredFilePath { get; init; }

    public required string SourceFolderPath { get; init; }

    public required string ManifestPath { get; init; }

    public required string Sha256FilePath { get; init; }

    public required long FileSizeBytes { get; init; }

    public required string FileSha256 { get; init; }

    public required DateTimeOffset ImportedAtUtc { get; init; }

    public string? AuditEventId { get; init; }

    public required string CorrelationId { get; init; }
}
