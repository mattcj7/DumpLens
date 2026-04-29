namespace DumpLens.Application.Sources;

public sealed record SourceImportRecord
{
    public required string Id { get; init; }

    public required string CaseId { get; init; }

    public required string SourceName { get; init; }

    public required string SourceType { get; init; }

    public string? Platform { get; init; }

    public string? OwnerPersonId { get; init; }

    public string? DeviceId { get; init; }

    public string? PlatformAccountId { get; init; }

    public string? ExtractionType { get; init; }

    public string? ProviderReturnType { get; init; }

    public required string OriginalFilename { get; init; }

    public string? OriginalFilePath { get; init; }

    public string? StoredFilePath { get; init; }

    public long? FileSizeBytes { get; init; }

    public required string FileSha256 { get; init; }

    public string? FileMd5 { get; init; }

    public string? ImportedByUserId { get; init; }

    public required DateTimeOffset ImportedAtUtc { get; init; }

    public required string ImportStatus { get; init; }

    public int RecordCount { get; init; }

    public int WarningCount { get; init; }

    public string? Notes { get; init; }

    public string? SourceMetadataJson { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
