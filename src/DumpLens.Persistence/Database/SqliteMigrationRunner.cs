using System.Data;
using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpLens.Persistence.Database;

public sealed class SqliteMigrationRunner
{
    private const string OperationName = "sqlite_migration_run";
    private readonly ILogger<SqliteMigrationRunner> _logger;
    private readonly IReadOnlyList<MigrationScript> _migrationScripts;

    public SqliteMigrationRunner(ILogger<SqliteMigrationRunner>? logger = null)
        : this(LoadEmbeddedScripts(typeof(AssemblyMarker).Assembly), logger)
    {
    }

    public SqliteMigrationRunner(IEnumerable<MigrationScript> migrationScripts, ILogger<SqliteMigrationRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(migrationScripts);

        var orderedScripts = migrationScripts
            .OrderBy(script => script.VersionNumber)
            .ThenBy(script => script.Version, StringComparer.Ordinal)
            .ToArray();

        if (orderedScripts.Length == 0)
        {
            throw new InvalidOperationException("At least one migration script is required.");
        }

        var duplicateVersion = orderedScripts
            .GroupBy(script => script.VersionNumber)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateVersion is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate migration version detected: {duplicateVersion.First().Version}.");
        }

        _migrationScripts = orderedScripts;
        _logger = logger ?? NullLogger<SqliteMigrationRunner>.Instance;
    }

    public IReadOnlyList<MigrationScript> MigrationScripts => _migrationScripts;

    public async Task RunMigrationsAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await using var connection = new SqliteConnection(connectionString);
        await RunMigrationsAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task RunMigrationsAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var correlationId = Guid.NewGuid().ToString("N");
        var openedHere = connection.State != ConnectionState.Open;
        var appliedCount = 0;
        var skippedCount = 0;
        string? failedVersion = null;
        string? failedMigrationName = null;

        _logger.LogInformation(
            "Migration run started. operation={Operation} correlation_id={CorrelationId} migration_count={MigrationCount}",
            OperationName,
            correlationId,
            _migrationScripts.Count);

        try
        {
            if (openedHere)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

            var schemaMigrationsExists = await SchemaMigrationsTableExistsAsync(connection, null, cancellationToken)
                .ConfigureAwait(false);
            var appliedMigrations = schemaMigrationsExists
                ? await LoadAppliedMigrationsAsync(connection, cancellationToken).ConfigureAwait(false)
                : new Dictionary<string, AppliedMigration>(StringComparer.Ordinal);

            foreach (var script in _migrationScripts)
            {
                if (appliedMigrations.TryGetValue(script.Version, out var appliedMigration))
                {
                    if (!string.Equals(appliedMigration.Checksum, script.Checksum, StringComparison.Ordinal))
                    {
                        failedVersion = script.Version;
                        failedMigrationName = script.Name;
                        throw new MigrationRunException(
                            $"Checksum mismatch detected for migration version '{script.Version}'.",
                            script.Version,
                            script.Name);
                    }

                    skippedCount++;
                    _logger.LogInformation(
                        "Migration skipped because it is already applied. operation={Operation} correlation_id={CorrelationId} version={Version} migration_name={MigrationName} checksum_prefix={ChecksumPrefix}",
                        OperationName,
                        correlationId,
                        script.Version,
                        script.Name,
                        script.Checksum[..12]);
                    continue;
                }

                try
                {
                    await ApplyMigrationAsync(connection, script, cancellationToken).ConfigureAwait(false);
                }
                catch (MigrationRunException)
                {
                    failedVersion = script.Version;
                    failedMigrationName = script.Name;
                    throw;
                }
                catch (Exception ex)
                {
                    failedVersion = script.Version;
                    failedMigrationName = script.Name;
                    throw new MigrationRunException(
                        $"Migration '{script.Version}_{script.Name}' failed to apply.",
                        script.Version,
                        script.Name,
                        ex);
                }

                appliedMigrations[script.Version] = new AppliedMigration(script.Version, script.Name, script.Checksum);
                appliedCount++;
                _logger.LogInformation(
                    "Migration applied. operation={Operation} correlation_id={CorrelationId} version={Version} migration_name={MigrationName} checksum_prefix={ChecksumPrefix}",
                    OperationName,
                    correlationId,
                    script.Version,
                    script.Name,
                    script.Checksum[..12]);
            }

            _logger.LogInformation(
                "Migration run completed. operation={Operation} correlation_id={CorrelationId} applied_count={AppliedCount} skipped_count={SkippedCount}",
                OperationName,
                correlationId,
                appliedCount,
                skippedCount);
        }
        catch (MigrationRunException ex)
        {
            _logger.LogError(
                ex,
                "Migration run failed. operation={Operation} correlation_id={CorrelationId} failed_version={FailedVersion} failed_migration_name={FailedMigrationName}",
                OperationName,
                correlationId,
                failedVersion ?? ex.Version ?? "unknown",
                failedMigrationName ?? ex.MigrationName ?? "unknown");
            throw;
        }
        catch (Exception ex)
        {
            var wrappedException = new MigrationRunException("Migration run failed.", failedVersion, failedMigrationName, ex);
            _logger.LogError(
                wrappedException,
                "Migration run failed. operation={Operation} correlation_id={CorrelationId} failed_version={FailedVersion} failed_migration_name={FailedMigrationName}",
                OperationName,
                correlationId,
                failedVersion ?? "unknown",
                failedMigrationName ?? "unknown");
            throw wrappedException;
        }
        finally
        {
            if (openedHere && connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static IReadOnlyList<MigrationScript> LoadEmbeddedScripts(Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name
            ?? throw new InvalidOperationException("Migration assembly name is unavailable.");
        var resourcePrefix = $"{assemblyName}.Migrations.";

        var scripts = assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal) &&
                           name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(name => MigrationScript.FromEmbeddedResource(assembly, name))
            .OrderBy(script => script.VersionNumber)
            .ThenBy(script => script.Version, StringComparer.Ordinal)
            .ToArray();

        if (scripts.Length == 0)
        {
            throw new InvalidOperationException("No embedded migration scripts were found.");
        }

        return scripts;
    }

    private static async Task ApplyMigrationAsync(
        SqliteConnection connection,
        MigrationScript script,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = script.Sql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var schemaMigrationsExists = await SchemaMigrationsTableExistsAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);

            if (!schemaMigrationsExists)
            {
                throw new MigrationRunException(
                    $"Migration '{script.Version}_{script.Name}' did not make schema_migrations available for tracking.",
                    script.Version,
                    script.Name);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO schema_migrations (version, name, applied_at_utc, checksum)
                    VALUES ($version, $name, $appliedAtUtc, $checksum);
                    """;
                command.Parameters.AddWithValue("$version", script.Version);
                command.Parameters.AddWithValue("$name", script.Name);
                command.Parameters.AddWithValue("$appliedAtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$checksum", script.Checksum);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<bool> SchemaMigrationsTableExistsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table' AND name = 'schema_migrations'
            );
            """;

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<Dictionary<string, AppliedMigration>> LoadAppliedMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var appliedMigrations = new Dictionary<string, AppliedMigration>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT version, name, checksum
            FROM schema_migrations
            ORDER BY version ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var version = reader.GetString(0);
            var name = reader.GetString(1);
            var checksum = reader.GetString(2);
            appliedMigrations[version] = new AppliedMigration(version, name, checksum);
        }

        return appliedMigrations;
    }

    private static async Task EnableForeignKeysAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var enableCommand = connection.CreateCommand())
        {
            enableCommand.CommandText = "PRAGMA foreign_keys = ON;";
            await enableCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var verifyCommand = connection.CreateCommand();
        verifyCommand.CommandText = "PRAGMA foreign_keys;";

        var result = await verifyCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (Convert.ToInt32(result, CultureInfo.InvariantCulture) != 1)
        {
            throw new MigrationRunException("SQLite foreign key enforcement could not be enabled for the migration run.");
        }
    }

    private sealed record AppliedMigration(string Version, string Name, string Checksum);
}
