namespace DumpLens.Application.Search;

public interface IMessageSearchIndexService
{
    Task<RebuildMessageSearchIndexResult> RebuildAsync(
        RebuildMessageSearchIndexRequest request,
        CancellationToken cancellationToken = default);

    Task<SearchMessagesResult> SearchAsync(
        SearchMessagesRequest request,
        CancellationToken cancellationToken = default);
}
