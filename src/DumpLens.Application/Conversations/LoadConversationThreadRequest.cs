namespace DumpLens.Application.Conversations;

public sealed record LoadConversationThreadRequest
{
    public required string CaseId { get; init; }

    public required string CaseDatabasePath { get; init; }

    public required string ConversationId { get; init; }
}
