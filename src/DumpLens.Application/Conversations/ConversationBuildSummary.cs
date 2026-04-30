namespace DumpLens.Application.Conversations;

public sealed record ConversationBuildSummary
{
    public required string ConversationId { get; init; }

    public required string Title { get; init; }

    public string? Platform { get; init; }

    public string? NormalizedParticipantKey { get; init; }

    public required string SourceThreadKeysJson { get; init; }

    public DateTimeOffset? StartTimeUtc { get; init; }

    public DateTimeOffset? EndTimeUtc { get; init; }

    public required int MessageCount { get; init; }

    public required int SourceCount { get; init; }

    public required int GapCount { get; init; }

    public required double PriorityScore { get; init; }

    public required string ReconciliationStatus { get; init; }

    public required string ReviewStatus { get; init; }
}
