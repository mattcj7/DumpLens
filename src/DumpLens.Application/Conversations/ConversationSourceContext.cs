namespace DumpLens.Application.Conversations;

public sealed record ConversationSourceContext
{
    public required string SourceImportId { get; init; }

    public required string SourceName { get; init; }

    public required string SourceType { get; init; }

    public string? Platform { get; init; }

    public required string OriginalFilename { get; init; }

    public string? SourceArtifactId { get; init; }

    public string? ArtifactLocator { get; init; }

    public string? ProviderMessageId { get; init; }

    public string? SourceThreadId { get; init; }

    public string? MessageHashPrefix { get; init; }
}
