namespace DumpLens.Application.Conversations;

public sealed record ConversationGroupCandidate
{
    public required string GroupKind { get; init; }

    public required string GroupKey { get; init; }

    public string? Platform { get; init; }

    public string? SourceThreadId { get; init; }

    public string? NormalizedParticipantKey { get; init; }

    public IReadOnlyList<string> MessageIds { get; init; } = [];

    public IReadOnlyList<string> ParticipantIdentityIds { get; init; } = [];
}
