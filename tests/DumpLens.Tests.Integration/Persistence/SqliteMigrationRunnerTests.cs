using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using DumpLens.Persistence.Database;

namespace DumpLens.Tests.Integration.Persistence;

public sealed class SqliteMigrationRunnerTests
{
    [Fact]
    public async Task RunMigrationsAsync_CreatesNewDatabaseAndStoresEmbeddedMigrations()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var runner = new SqliteMigrationRunner();

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

        Assert.True(File.Exists(tempDatabase.DatabasePath));
        Assert.True(await TableExistsAsync(connection, "schema_migrations"));

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT version, name, checksum
            FROM schema_migrations
            ORDER BY version ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("0001", reader.GetString(0));
        Assert.Equal("bootstrap_schema_migrations_support", reader.GetString(1));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("0002", reader.GetString(0));
        Assert.Equal("initial_core_schema", reader.GetString(1));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("0003", reader.GetString(0));
        Assert.Equal("communication_schema", reader.GetString(1));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("0004", reader.GetString(0));
        Assert.Equal("message_search_index", reader.GetString(1));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task RunMigrationsAsync_AppliesMigrationsInAscendingOrderAndSkipsMatchingReRuns()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var logger = new TestLogger<SqliteMigrationRunner>();
        var runner = new SqliteMigrationRunner(
            new[]
            {
                new MigrationScript("0002", "create_order_probe", "CREATE TABLE order_probe (id INTEGER NOT NULL PRIMARY KEY);"),
                CreateBootstrapMigration()
            },
            logger);

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);
        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

        Assert.True(await TableExistsAsync(connection, "order_probe"));

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT version FROM schema_migrations ORDER BY version ASC;";
        var versions = new List<string>();
        await using (var reader = await versionCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                versions.Add(reader.GetString(0));
            }
        }

        Assert.Equal(new[] { "0001", "0002" }, versions);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        Assert.Equal(2L, (long)(await countCommand.ExecuteScalarAsync())!);

        Assert.Contains(logger.Entries, entry => entry.Message.Contains("Migration applied.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("Migration skipped because it is already applied.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunMigrationsAsync_ThrowsWhenAnAppliedMigrationChecksumChanges()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var originalRunner = new SqliteMigrationRunner(
            new[]
            {
                CreateBootstrapMigration("-- original"),
            });

        await originalRunner.RunMigrationsAsync(tempDatabase.ConnectionString);

        var modifiedRunner = new SqliteMigrationRunner(
            new[]
            {
                CreateBootstrapMigration("-- modified"),
            });

        var exception = await Assert.ThrowsAsync<MigrationRunException>(
            () => modifiedRunner.RunMigrationsAsync(tempDatabase.ConnectionString));

        Assert.Equal("0001", exception.Version);
        Assert.Equal("bootstrap_schema_migrations_support", exception.MigrationName);
        Assert.Contains("Checksum mismatch detected", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunMigrationsAsync_RollsBackFailedMigrationAndWrapsTheErrorSafely()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var runner = new SqliteMigrationRunner(
            new[]
            {
                CreateBootstrapMigration(),
                new MigrationScript(
                    "0002",
                    "broken_probe",
                    """
                    CREATE TABLE broken_probe (id INTEGER NOT NULL PRIMARY KEY);
                    THIS IS NOT VALID SQL;
                    """)
            });

        var exception = await Assert.ThrowsAsync<MigrationRunException>(
            () => runner.RunMigrationsAsync(tempDatabase.ConnectionString));

        Assert.Equal("0002", exception.Version);
        Assert.Equal("broken_probe", exception.MigrationName);
        Assert.DoesNotContain("THIS IS NOT VALID SQL", exception.Message, StringComparison.Ordinal);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

        Assert.False(await TableExistsAsync(connection, "broken_probe"));

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        Assert.Equal(1L, (long)(await countCommand.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task RunMigrationsAsync_EmitsEvidenceSafeStructuredLogs()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var logger = new TestLogger<SqliteMigrationRunner>();
        const string sensitiveToken = "TOP_SECRET_EVIDENCE_TOKEN";
        var runner = new SqliteMigrationRunner(new[] { CreateBootstrapMigration() }, logger);

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);
        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        var failingRunner = new SqliteMigrationRunner(
            new[]
            {
                CreateBootstrapMigration(),
                new MigrationScript(
                    "0002",
                    "failure_probe",
                    $"""
                    -- {sensitiveToken}
                    CREATE TABLE failure_probe (id INTEGER NOT NULL PRIMARY KEY);
                    INVALID TOKEN;
                    """)
            },
            logger);

        await Assert.ThrowsAsync<MigrationRunException>(() => failingRunner.RunMigrationsAsync(tempDatabase.ConnectionString));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Migration run started.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Migration applied.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Migration skipped because it is already applied.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Migration run completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Migration run failed.", StringComparison.Ordinal));
        Assert.All(logger.Entries, entry => Assert.DoesNotContain(sensitiveToken, entry.Message, StringComparison.Ordinal));
        Assert.All(
            logger.Entries,
            entry =>
            {
                Assert.True(entry.State.ContainsKey("Operation"));
                Assert.True(entry.State.ContainsKey("CorrelationId"));
            });
    }

    [Fact]
    public async Task RunMigrationsAsync_EnablesForeignKeysOnTheMigrationConnection()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create(foreignKeysEnabled: false);
        await using var connection = new SqliteConnection(tempDatabase.CreateConnectionString(foreignKeysEnabled: false));
        await connection.OpenAsync();

        await SetForeignKeysAsync(connection, enabled: false);
        Assert.Equal(0, await GetForeignKeysSettingAsync(connection));

        var runner = new SqliteMigrationRunner(new[] { CreateBootstrapMigration() });

        await runner.RunMigrationsAsync(connection);

        Assert.Equal(1, await GetForeignKeysSettingAsync(connection));
    }

    private static MigrationScript CreateBootstrapMigration(string? prefixComment = null)
    {
        var sql = string.IsNullOrWhiteSpace(prefixComment)
            ? BootstrapSql
            : $"{prefixComment}{Environment.NewLine}{BootstrapSql}";

        return new MigrationScript("0001", "bootstrap_schema_migrations_support", sql);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table' AND name = $tableName
            );
            """;
        command.Parameters.AddWithValue("$tableName", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<int> GetForeignKeysSettingAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task SetForeignKeysAsync(SqliteConnection connection, bool enabled)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = enabled ? "PRAGMA foreign_keys = ON;" : "PRAGMA foreign_keys = OFF;";
        await command.ExecuteNonQueryAsync();
    }

    private const string BootstrapSql =
        """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            version TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL,
            applied_at_utc TEXT NOT NULL,
            checksum TEXT NOT NULL
        );
        """;

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NoOpScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var structuredState = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (state is IEnumerable<KeyValuePair<string, object?>> keyValuePairs)
            {
                foreach (var keyValuePair in keyValuePairs)
                {
                    if (keyValuePair.Key == "{OriginalFormat}")
                    {
                        continue;
                    }

                    structuredState[keyValuePair.Key] = keyValuePair.Value;
                }
            }

            Entries.Add(new LogEntry(logLevel, formatter(state, exception), structuredState, exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> State,
        Exception? Exception);

    private sealed class NoOpScope : IDisposable
    {
        public static NoOpScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
