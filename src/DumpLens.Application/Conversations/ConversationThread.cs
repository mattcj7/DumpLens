namespace DumpLens.Application.Conversations;

public sealed record ConversationThread
{
    public required string ConversationId { get; init; }

    public IReadOnlyList<ConversationThreadMessage> Messages { get; init; } = [];
}
