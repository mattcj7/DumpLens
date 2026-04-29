namespace DumpLens.Application.CallImports;

public interface ICallImportService
{
    Task<ImportCallsResult> ImportAsync(
        ImportCallsRequest request,
        CancellationToken cancellationToken = default);
}
