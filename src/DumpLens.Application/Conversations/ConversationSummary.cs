namespace DumpLens.Application.Conversations;

public sealed record ConversationSummary
{
    public required string ConversationId { get; init; }

    public required string Title { get; init; }

    public string? Platform { get; init; }

    public DateTimeOffset? StartTimeUtc { get; init; }

    public DateTimeOffset? EndTimeUtc { get; init; }

    public int MessageCount { get; init; }

    public int SourceCount { get; init; }

    public int GapCount { get; init; }

    public double PriorityScore { get; init; }

    public required string ReconciliationStatus { get; init; }

    public required string ReviewStatus { get; init; }
}
