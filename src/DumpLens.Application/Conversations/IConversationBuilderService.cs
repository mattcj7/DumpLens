namespace DumpLens.Application.Conversations;

public interface IConversationBuilderService
{
    Task<BuildConversationsResult> BuildAsync(
        BuildConversationsRequest request,
        CancellationToken cancellationToken = default);
}
