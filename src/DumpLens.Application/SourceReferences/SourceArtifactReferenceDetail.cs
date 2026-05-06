namespace DumpLens.Application.SourceReferences;

public sealed record SourceArtifactReferenceDetail
{
    public required string SourceArtifactId { get; init; }

    public required string ArtifactType { get; init; }

    public string? ArtifactLocator { get; init; }

    public bool HasOriginalMetadata { get; init; }
}
