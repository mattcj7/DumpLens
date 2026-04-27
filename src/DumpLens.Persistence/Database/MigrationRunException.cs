namespace DumpLens.Persistence.Database;

public sealed class MigrationRunException : Exception
{
    public MigrationRunException(string message)
        : base(message)
    {
    }

    public MigrationRunException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
