namespace DumpLens.Application.SourceReferences;

public sealed record MessageSourceReferenceDetail
{
    public required string MessageId { get; init; }

    public string? SourceArtifactId { get; init; }

    public string? ProviderMessageId { get; init; }

    public string? SourceThreadId { get; init; }

    public DateTimeOffset? EventTimeUtc { get; init; }

    public string? DeletedStatus { get; init; }

    public string? MessageHashPrefix { get; init; }

    public bool HasOriginalMetadata { get; init; }
}
