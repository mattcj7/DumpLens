namespace DumpLens.Application.SourceReferences;

public interface ISourceReferenceReader
{
    Task<SourceReferenceDetail?> LoadAsync(
        LoadSourceReferenceRequest request,
        CancellationToken cancellationToken = default);
}
