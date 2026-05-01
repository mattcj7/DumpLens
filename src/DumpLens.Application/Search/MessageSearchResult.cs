namespace DumpLens.Application.Search;

public sealed record MessageSearchResult
{
    public required string CaseId { get; init; }

    public required string MessageId { get; init; }

    public string? ConversationId { get; init; }

    public required string SourceImportId { get; init; }

    public required string SourceArtifactId { get; init; }

    public string? ProviderMessageId { get; init; }

    public string? SourceThreadId { get; init; }

    public DateTimeOffset? EventTimeUtc { get; init; }

    public string? Direction { get; init; }

    public string? Platform { get; init; }

    public required string DeletedStatus { get; init; }

    public string? Snippet { get; init; }

    public double? Rank { get; init; }
}
