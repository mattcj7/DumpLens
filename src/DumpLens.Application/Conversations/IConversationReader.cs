namespace DumpLens.Application.Conversations;

public interface IConversationReader
{
    Task<IReadOnlyList<ConversationSummary>> GetSummariesAsync(
        LoadConversationSummariesRequest request,
        CancellationToken cancellationToken = default);

    Task<ConversationThread?> GetThreadAsync(
        LoadConversationThreadRequest request,
        CancellationToken cancellationToken = default);
}
