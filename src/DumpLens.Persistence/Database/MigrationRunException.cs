namespace DumpLens.Persistence.Database;

public sealed class MigrationRunException : Exception
{
    public MigrationRunException(string message, string? version = null, string? migrationName = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Version = version;
        MigrationName = migrationName;
    }

    public string? Version { get; }

    public string? MigrationName { get; }
}
