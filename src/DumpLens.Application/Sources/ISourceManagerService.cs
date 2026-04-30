namespace DumpLens.Application.Sources;

public interface ISourceManagerService
{
    Task<IReadOnlyList<SourceImportSummary>> GetSummariesAsync(
        LoadSourceImportSummariesRequest request,
        CancellationToken cancellationToken = default);

    Task<SourceImportDetail?> GetDetailAsync(
        LoadSourceImportDetailRequest request,
        CancellationToken cancellationToken = default);
}
