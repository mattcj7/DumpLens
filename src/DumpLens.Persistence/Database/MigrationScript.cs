namespace DumpLens.Persistence.Database;

public sealed record MigrationScript(string Version, string Name, string Sql, string Checksum);
