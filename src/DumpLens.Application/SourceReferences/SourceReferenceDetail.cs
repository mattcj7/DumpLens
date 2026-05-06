namespace DumpLens.Application.SourceReferences;

public sealed record SourceReferenceDetail
{
    public required string CaseId { get; init; }

    public required string SourceImportId { get; init; }

    public required string SourceName { get; init; }

    public required string SourceType { get; init; }

    public string? Platform { get; init; }

    public required string ImportStatus { get; init; }

    public required string OriginalFilename { get; init; }

    public string? StoredRelativePath { get; init; }

    public long? FileSizeBytes { get; init; }

    public required string FileSha256 { get; init; }

    public required DateTimeOffset ImportedAtUtc { get; init; }

    public bool HasSourceMetadata { get; init; }

    public bool WasArtifactReferenceRequested { get; init; }

    public bool WasMessageReferenceRequested { get; init; }

    public SourceArtifactReferenceDetail? ArtifactReference { get; init; }

    public MessageSourceReferenceDetail? MessageReference { get; init; }
}
