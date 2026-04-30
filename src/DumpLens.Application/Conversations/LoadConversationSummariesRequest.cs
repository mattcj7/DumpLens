namespace DumpLens.Application.Conversations;

public sealed record LoadConversationSummariesRequest
{
    public required string CaseId { get; init; }

    public required string CaseDatabasePath { get; init; }
}
