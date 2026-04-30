namespace DumpLens.Application.Conversations;

public sealed record BuildConversationsResult
{
    public required string CaseId { get; init; }

    public required int ConversationCountCreated { get; init; }

    public required int ConversationCountUpdated { get; init; }

    public required int ParticipantCountCreated { get; init; }

    public required int MessageCountAssigned { get; init; }

    public required int UnassignedMessageCount { get; init; }

    public IReadOnlyList<ConversationBuildSummary> ConversationSummaries { get; init; } = [];

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}
