using Microsoft.Data.Sqlite;

namespace DumpLens.Tests.Integration.Persistence;

internal sealed class TemporarySqliteDatabase : IDisposable
{
    private TemporarySqliteDatabase(string directoryPath, string databasePath, bool foreignKeysEnabled)
    {
        DirectoryPath = directoryPath;
        DatabasePath = databasePath;
        ConnectionString = BuildConnectionString(databasePath, foreignKeysEnabled);
    }

    public string ConnectionString { get; }

    public string DatabasePath { get; }

    private string DirectoryPath { get; }

    public static TemporarySqliteDatabase Create(bool foreignKeysEnabled = true)
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "DumpLens.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        var databasePath = Path.Combine(directoryPath, "case.db");
        return new TemporarySqliteDatabase(directoryPath, databasePath, foreignKeysEnabled);
    }

    public string CreateConnectionString(bool foreignKeysEnabled = true)
    {
        return BuildConnectionString(DatabasePath, foreignKeysEnabled);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private static string BuildConnectionString(string databasePath, bool foreignKeysEnabled)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = foreignKeysEnabled,
            Pooling = false
        }.ToString();
    }
}
