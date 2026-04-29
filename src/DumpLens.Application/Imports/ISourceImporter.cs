namespace DumpLens.Application.Imports;

public interface ISourceImporter
{
    ImportSourceKind SourceKind { get; }

    bool CanHandle(string filePath);

    Task<ImportProbeResult> ProbeAsync(
        ImportProbeRequest request,
        CancellationToken cancellationToken = default);

    Task<ImportPreviewResult> PreviewAsync(
        ImportPreviewRequest request,
        CancellationToken cancellationToken = default);
}
