using Microsoft.Data.Sqlite;
using DumpLens.Persistence.Database;

namespace DumpLens.Tests.Integration.Persistence;

public sealed class InitialCoreSchemaMigrationTests
{
    private static readonly string[] ExpectedTables =
    {
        "schema_migrations",
        "cases",
        "app_users",
        "case_users",
        "source_imports",
        "source_artifacts",
        "import_mappings",
        "import_warnings",
        "audit_events",
        "app_settings"
    };

    private static readonly string[] ExpectedIndexes =
    {
        "idx_cases_case_number",
        "idx_cases_status",
        "idx_source_imports_case",
        "idx_source_imports_hash",
        "idx_source_imports_type",
        "idx_source_artifacts_source",
        "idx_source_artifacts_case",
        "idx_source_artifacts_provider_id",
        "idx_import_warnings_source",
        "idx_import_warnings_status",
        "idx_audit_events_case_time",
        "idx_audit_events_entity",
        "idx_audit_events_action"
    };

    [Fact]
    public async Task RunMigrationsAsync_CreatesCoreSchemaAndRecordsBootstrapThenCoreMigration()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var runner = new SqliteMigrationRunner();

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

        Assert.Equal(1, await GetForeignKeysSettingAsync(connection));

        foreach (var tableName in ExpectedTables)
        {
            Assert.True(await TableExistsAsync(connection, tableName), $"Expected table '{tableName}' to exist.");
        }

        foreach (var indexName in ExpectedIndexes)
        {
            Assert.True(await IndexExistsAsync(connection, indexName), $"Expected index '{indexName}' to exist.");
        }

        var migrations = await GetAppliedMigrationsAsync(connection);
        Assert.Equal(
            new[]
            {
                ("0001", "bootstrap_schema_migrations_support"),
                ("0002", "initial_core_schema")
            },
            migrations);
    }

    [Fact]
    public async Task CaseUsersTable_RejectsMissingCaseAndMissingUserWhenForeignKeysAreEnabled()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var runner = new SqliteMigrationRunner();

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

        await InsertCaseAsync(connection, "case-1");
        await InsertUserAsync(connection, "user-1");

        var missingCaseException = await Assert.ThrowsAsync<SqliteException>(
            () => InsertCaseUserAsync(connection, "case-user-missing-case", "missing-case", "user-1"));
        Assert.Contains("FOREIGN KEY constraint failed", missingCaseException.Message, StringComparison.Ordinal);

        var missingUserException = await Assert.ThrowsAsync<SqliteException>(
            () => InsertCaseUserAsync(connection, "case-user-missing-user", "case-1", "missing-user"));
        Assert.Contains("FOREIGN KEY constraint failed", missingUserException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceArtifactsTable_CascadesWhenParentSourceImportIsDeleted()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var runner = new SqliteMigrationRunner();

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

        await InsertCaseAsync(connection, "case-1");
        await InsertSourceImportAsync(connection, "import-1", "case-1");
        await InsertSourceArtifactAsync(connection, "artifact-1", "case-1", "import-1");

        Assert.Equal(1L, await CountRowsAsync(connection, "source_artifacts"));

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.CommandText = "DELETE FROM source_imports WHERE id = $id;";
            deleteCommand.Parameters.AddWithValue("$id", "import-1");
            await deleteCommand.ExecuteNonQueryAsync();
        }

        Assert.Equal(0L, await CountRowsAsync(connection, "source_artifacts"));
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table' AND name = $name
            );
            """;
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> IndexExistsAsync(SqliteConnection connection, string indexName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_master
                WHERE type = 'index' AND name = $name
            );
            """;
        command.Parameters.AddWithValue("$name", indexName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
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

    private static async Task<int> GetForeignKeysSettingAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<long> CountRowsAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task InsertCaseAsync(SqliteConnection connection, string caseId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO cases (
                id,
                case_number,
                title,
                case_status,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                $caseNumber,
                $title,
                'open',
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", caseId);
        command.Parameters.AddWithValue("$caseNumber", "DL-001");
        command.Parameters.AddWithValue("$title", "Synthetic Case");
        command.Parameters.AddWithValue("$createdAtUtc", "2026-01-01T00:00:00Z");
        command.Parameters.AddWithValue("$updatedAtUtc", "2026-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertUserAsync(SqliteConnection connection, string userId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO app_users (
                id,
                display_name,
                username,
                role,
                is_active,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                $displayName,
                $username,
                'investigator',
                1,
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", userId);
        command.Parameters.AddWithValue("$displayName", "Synthetic Investigator");
        command.Parameters.AddWithValue("$username", "synthetic.user");
        command.Parameters.AddWithValue("$createdAtUtc", "2026-01-01T00:00:00Z");
        command.Parameters.AddWithValue("$updatedAtUtc", "2026-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertCaseUserAsync(SqliteConnection connection, string caseUserId, string caseId, string userId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO case_users (
                id,
                case_id,
                user_id,
                case_role,
                created_at_utc
            )
            VALUES (
                $id,
                $caseId,
                $userId,
                'lead',
                $createdAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", caseUserId);
        command.Parameters.AddWithValue("$caseId", caseId);
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$createdAtUtc", "2026-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertSourceImportAsync(SqliteConnection connection, string sourceImportId, string caseId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO source_imports (
                id,
                case_id,
                source_name,
                source_type,
                original_filename,
                file_sha256,
                imported_at_utc,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                $caseId,
                'Synthetic Import',
                'device_export',
                'synthetic.csv',
                'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                $importedAtUtc,
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", sourceImportId);
        command.Parameters.AddWithValue("$caseId", caseId);
        command.Parameters.AddWithValue("$importedAtUtc", "2026-01-01T00:00:00Z");
        command.Parameters.AddWithValue("$createdAtUtc", "2026-01-01T00:00:00Z");
        command.Parameters.AddWithValue("$updatedAtUtc", "2026-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertSourceArtifactAsync(
        SqliteConnection connection,
        string artifactId,
        string caseId,
        string sourceImportId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO source_artifacts (
                id,
                case_id,
                source_import_id,
                artifact_type,
                created_at_utc
            )
            VALUES (
                $id,
                $caseId,
                $sourceImportId,
                'message',
                $createdAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", artifactId);
        command.Parameters.AddWithValue("$caseId", caseId);
        command.Parameters.AddWithValue("$sourceImportId", sourceImportId);
        command.Parameters.AddWithValue("$createdAtUtc", "2026-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }
}
