namespace DumpLens.Application.FileHashing;

public interface IFileHashService
{
    Task<FileHashResult> ComputeHashAsync(
        FileHashRequest request,
        CancellationToken cancellationToken = default);

    Task<string> WriteSha256FileAsync(
        FileHashResult result,
        string targetFolderPath,
        string outputFileName = "sha256.txt",
        CancellationToken cancellationToken = default);
}
