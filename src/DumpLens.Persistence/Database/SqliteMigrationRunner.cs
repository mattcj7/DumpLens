using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DumpLens.Persistence.Database;

public sealed class SqliteMigrationRunner
{
    private const string MigrationResourcePrefix = "DumpLens.Persistence.Migrations.";
    private readonly ILogger<SqliteMigrationRunner> _logger;

    public SqliteMigrationRunner(ILogger<SqliteMigrationRunner> logger)
    {
        _logger = logger;
    }

    public async Task RunMigrationsAsync(
        string connectionString,
        IReadOnlyCollection<MigrationScript>? scripts = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var resolvedScripts = scripts ?? await LoadEmbeddedScriptsAsync(cancellationToken);

        _logger.LogInformation(
            "Database migration run started. MigrationCount={MigrationCount}",
            resolvedScripts.Count);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await EnsureMigrationTableAsync(connection, cancellationToken);

        try
        {
            foreach (var script in resolvedScripts.OrderBy(static x => x.Version, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var existingChecksum = await GetAppliedChecksumAsync(connection, script.Version, cancellationToken);
                if (existingChecksum is not null)
                {
                    if (!string.Equals(existingChecksum, script.Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new MigrationRunException(
                            $"Migration checksum mismatch for version '{script.Version}'.");
                    }

                    _logger.LogInformation(
                        "Database migration skipped because it was already applied. MigrationVersion={MigrationVersion} MigrationName={MigrationName}",
                        script.Version,
                        script.Name);

                    continue;
                }

                await ApplyMigrationAsync(connection, script, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Database migration run failed.");
            throw;
        }

        _logger.LogInformation(
            "Database migration run completed. MigrationCount={MigrationCount}",
            resolvedScripts.Count);
    }

    internal static async Task<IReadOnlyCollection<MigrationScript>> LoadEmbeddedScriptsAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(SqliteMigrationRunner).Assembly;
        var resources = assembly
            .GetManifestResourceNames()
            .Where(static name => name.StartsWith(MigrationResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        var scripts = new List<MigrationScript>(resources.Length);

        foreach (var resourceName in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = assembly.GetManifestResourceStream(resourceName)
                                     ?? throw new MigrationRunException($"Missing migration resource '{resourceName}'.");
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
            var sql = await reader.ReadToEndAsync(cancellationToken);

            var fileName = resourceName[MigrationResourcePrefix.Length..];
            var separatorIndex = fileName.IndexOf('_');
            if (separatorIndex <= 0)
            {
                throw new MigrationRunException($"Migration file name '{fileName}' is invalid.");
            }

            var version = fileName[..separatorIndex];
            var name = fileName[(separatorIndex + 1)..].Replace(".sql", string.Empty, StringComparison.OrdinalIgnoreCase);
            scripts.Add(new MigrationScript(version, name, sql, ComputeChecksum(sql)));
        }

        return scripts;
    }

    public static string ComputeChecksum(string sql)
    {
        var bytes = Encoding.UTF8.GetBytes(sql);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task EnsureMigrationTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
                           CREATE TABLE IF NOT EXISTS schema_migrations (
                               version TEXT PRIMARY KEY,
                               name TEXT NOT NULL,
                               applied_at_utc TEXT NOT NULL,
                               checksum TEXT NOT NULL
                           );
                           """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ApplyMigrationAsync(SqliteConnection connection, MigrationScript script, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var migrationCommand = connection.CreateCommand();
            migrationCommand.Transaction = transaction;
            migrationCommand.CommandText = script.Sql;
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken);

            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                "INSERT INTO schema_migrations(version, name, applied_at_utc, checksum) VALUES ($version, $name, $appliedAtUtc, $checksum);";
            insertCommand.Parameters.AddWithValue("$version", script.Version);
            insertCommand.Parameters.AddWithValue("$name", script.Name);
            insertCommand.Parameters.AddWithValue("$appliedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            insertCommand.Parameters.AddWithValue("$checksum", script.Checksum);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Database migration applied. MigrationVersion={MigrationVersion} MigrationName={MigrationName}",
                script.Version,
                script.Name);
        }
        catch (SqliteException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new MigrationRunException(
                $"Failed to apply migration '{script.Version}_{script.Name}'.", ex);
        }
    }

    private static async Task<string?> GetAppliedChecksumAsync(
        SqliteConnection connection,
        string version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT checksum FROM schema_migrations WHERE version = $version LIMIT 1;";
        command.Parameters.AddWithValue("$version", version);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }
}
