namespace DumpLens.Application.MessageImports;

public interface IMessageImportService
{
    Task<ImportMessagesResult> ImportAsync(
        ImportMessagesRequest request,
        CancellationToken cancellationToken = default);
}
