using Microsoft.Data.Sqlite;
using DumpLens.Persistence.Database;

namespace DumpLens.Tests.Integration.Persistence;

public sealed class MessageSearchIndexMigrationTests
{
    [Fact]
    public async Task RunMigrationsAsync_CreatesMessageSearchIndexVirtualTableAndRecordsMigration()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var runner = new SqliteMigrationRunner();

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

        var tableSql = await LoadObjectSqlAsync(connection, "message_search_index", "table");
        Assert.NotNull(tableSql);
        Assert.Contains("fts5", tableSql!, StringComparison.OrdinalIgnoreCase);

        var migrations = await GetAppliedMigrationsAsync(connection);
        Assert.Equal(
            new[]
            {
                ("0001", "bootstrap_schema_migrations_support"),
                ("0002", "initial_core_schema"),
                ("0003", "communication_schema"),
                ("0004", "message_search_index")
            },
            migrations);
    }

    private static async Task<string?> LoadObjectSqlAsync(SqliteConnection connection, string objectName, string objectType)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT sql
            FROM sqlite_master
            WHERE name = $name
              AND type = $type
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$name", objectName);
        command.Parameters.AddWithValue("$type", objectType);

        var result = await command.ExecuteScalarAsync();
        return result as string;
    }

    private static async Task<List<(string Version, string Name)>> GetAppliedMigrationsAsync(SqliteConnection connection)
    {
        var migrations = new List<(string Version, string Name)>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT version, name
            FROM schema_migrations
            ORDER BY version ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            migrations.Add((reader.GetString(0), reader.GetString(1)));
        }

        return migrations;
    }
}
