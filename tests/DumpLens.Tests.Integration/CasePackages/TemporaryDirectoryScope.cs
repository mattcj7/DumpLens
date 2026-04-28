namespace DumpLens.Tests.Integration.CasePackages;

internal sealed class TemporaryDirectoryScope : IDisposable
{
    private TemporaryDirectoryScope(string directoryPath)
    {
        DirectoryPath = directoryPath;
    }

    public string DirectoryPath { get; }

    public static TemporaryDirectoryScope Create()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "DumpLens.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return new TemporaryDirectoryScope(directoryPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
