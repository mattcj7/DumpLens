using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using DumpLens.Persistence.Database;

namespace DumpLens.Tests.Integration.Persistence;

public sealed class SqliteMigrationRunnerTests
{
    [Fact]
    public async Task RunMigrationsAsync_AppliesEmbeddedMigrationsAndStoresChecksums()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"dumplens-migrations-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        var logger = new TestLogger<SqliteMigrationRunner>();
        var runner = new SqliteMigrationRunner(logger);

        try
        {
            await runner.RunMigrationsAsync(connectionString);

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var migrationCount = await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM schema_migrations;");
            Assert.True(migrationCount >= 1);

            var firstVersion = await ExecuteScalarAsync<string>(connection, "SELECT version FROM schema_migrations ORDER BY version LIMIT 1;");
            var checksum = await ExecuteScalarAsync<string>(connection, "SELECT checksum FROM schema_migrations WHERE version = $version;", ("$version", firstVersion));
            Assert.False(string.IsNullOrWhiteSpace(checksum));

            Assert.Contains(logger.Entries, e => e.LogLevel == LogLevel.Information && e.Message.Contains("run started", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(logger.Entries, e => e.LogLevel == LogLevel.Information && e.Message.Contains("migration applied", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(logger.Entries, e => e.LogLevel == LogLevel.Information && e.Message.Contains("run completed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task RunMigrationsAsync_IsIdempotentWhenRunTwice()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"dumplens-migrations-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        var logger = new TestLogger<SqliteMigrationRunner>();
        var runner = new SqliteMigrationRunner(logger);

        try
        {
            await runner.RunMigrationsAsync(connectionString);
            await runner.RunMigrationsAsync(connectionString);

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var migrationCount = await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM schema_migrations;");
            var distinctCount = await ExecuteScalarAsync<long>(connection, "SELECT COUNT(DISTINCT version) FROM schema_migrations;");

            Assert.Equal(distinctCount, migrationCount);
            Assert.Contains(logger.Entries, e => e.LogLevel == LogLevel.Information && e.Message.Contains("already applied", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task RunMigrationsAsync_FailsSafelyForInvalidMigrationAndLogsFailure()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"dumplens-migrations-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        var logger = new TestLogger<SqliteMigrationRunner>();
        var runner = new SqliteMigrationRunner(logger);

        var customScripts = new[]
        {
            new MigrationScript("0001", "bootstrap", "CREATE TABLE IF NOT EXISTS custom_table (id INTEGER PRIMARY KEY);", SqliteMigrationRunner.ComputeChecksum("CREATE TABLE IF NOT EXISTS custom_table (id INTEGER PRIMARY KEY);")),
            new MigrationScript("0002", "broken", "CREAT TABLE this_will_fail (id INTEGER PRIMARY KEY);", SqliteMigrationRunner.ComputeChecksum("CREAT TABLE this_will_fail (id INTEGER PRIMARY KEY);"))
        };

        try
        {
            var ex = await Assert.ThrowsAsync<MigrationRunException>(() => runner.RunMigrationsAsync(connectionString, customScripts));
            Assert.Contains("Failed to apply migration", ex.Message, StringComparison.OrdinalIgnoreCase);

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var migrationCount = await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM schema_migrations;");
            Assert.Equal(1, migrationCount);
            Assert.Contains(logger.Entries, e => e.LogLevel == LogLevel.Error && e.Message.Contains("run failed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static async Task<T> ExecuteScalarAsync<T>(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var raw = await command.ExecuteScalarAsync();
        Assert.NotNull(raw);
        return (T)Convert.ChangeType(raw, typeof(T));
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);
}
