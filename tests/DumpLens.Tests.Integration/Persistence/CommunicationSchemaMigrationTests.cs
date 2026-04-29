using Microsoft.Data.Sqlite;
using DumpLens.Persistence.Database;

namespace DumpLens.Tests.Integration.Persistence;

public sealed class CommunicationSchemaMigrationTests
{
    private static readonly string[] ExpectedTables =
    {
        "persons",
        "identities",
        "identity_links",
        "devices",
        "platform_accounts",
        "messages",
        "message_recipients",
        "conversations",
        "conversation_participants",
        "calls",
        "attachments"
    };

    private static readonly string[] ExpectedIndexes =
    {
        "idx_persons_case",
        "idx_persons_display_name",
        "idx_persons_role",
        "idx_identities_case",
        "idx_identities_type",
        "idx_identities_norm",
        "idx_identities_person",
        "idx_identity_links_case",
        "idx_identity_links_source",
        "idx_identity_links_status",
        "idx_devices_case",
        "idx_devices_owner",
        "idx_platform_accounts_case",
        "idx_platform_accounts_platform",
        "idx_platform_accounts_username",
        "idx_messages_case_time",
        "idx_messages_source",
        "idx_messages_sender",
        "idx_messages_conversation",
        "idx_messages_body_hash",
        "idx_messages_deleted_status",
        "idx_messages_reconciliation_status",
        "idx_message_recipients_message",
        "idx_message_recipients_identity",
        "idx_conversations_case",
        "idx_conversations_time",
        "idx_conversations_priority",
        "idx_conversation_participants_conv",
        "idx_conversation_participants_identity",
        "idx_calls_case_time",
        "idx_calls_caller",
        "idx_calls_callee",
        "idx_calls_source",
        "idx_attachments_case",
        "idx_attachments_message",
        "idx_attachments_hash"
    };

    [Fact]
    public async Task RunMigrationsAsync_CreatesCommunicationSchemaAndRecordsAllMigrationsInOrder()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var runner = new SqliteMigrationRunner();

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

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
                ("0002", "initial_core_schema"),
                ("0003", "communication_schema")
            },
            migrations);
    }

    [Fact]
    public async Task IdentitiesTable_RejectsMissingCaseIdWhenForeignKeysAreEnabled()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var runner = new SqliteMigrationRunner();

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => InsertIdentityAsync(connection, "identity-missing-case", "missing-case", "phone", "+1-555-0100"));

        Assert.Contains("FOREIGN KEY constraint failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MessageRecipientsTable_RejectsMissingMessageId()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var runner = new SqliteMigrationRunner();

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

        await InsertCaseAsync(connection, "case-1");
        await InsertIdentityAsync(connection, "identity-1", "case-1", "phone", "+1-555-0101");

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => InsertMessageRecipientAsync(connection, "recipient-1", "case-1", "missing-message", "identity-1"));

        Assert.Contains("FOREIGN KEY constraint failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdentityLinksTable_RejectsRowsWithoutTargetIdentityOrTargetPerson()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var runner = new SqliteMigrationRunner();

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

        await InsertCaseAsync(connection, "case-1");
        await InsertIdentityAsync(connection, "identity-1", "case-1", "phone", "+1-555-0102");

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => InsertIdentityLinkAsync(connection, "link-1", "case-1", "identity-1"));

        Assert.Contains("CHECK constraint failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MessagesTable_CascadesWhenParentSourceImportIsDeleted()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var runner = new SqliteMigrationRunner();

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

        await InsertCaseAsync(connection, "case-1");
        await InsertSourceImportAsync(connection, "import-1", "case-1");
        await InsertMessageAsync(connection, "message-1", "case-1", "import-1");

        Assert.Equal(1L, await CountRowsAsync(connection, "messages"));

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.CommandText = "DELETE FROM source_imports WHERE id = $id;";
            deleteCommand.Parameters.AddWithValue("$id", "import-1");
            await deleteCommand.ExecuteNonQueryAsync();
        }

        Assert.Equal(0L, await CountRowsAsync(connection, "messages"));
    }

    [Fact]
    public async Task AttachmentsTable_SetsLinkedMessageIdToNullWhenMessageIsDeleted()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        var runner = new SqliteMigrationRunner();

        await runner.RunMigrationsAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();

        await InsertCaseAsync(connection, "case-1");
        await InsertSourceImportAsync(connection, "import-1", "case-1");
        await InsertMessageAsync(connection, "message-1", "case-1", "import-1");
        await InsertAttachmentAsync(connection, "attachment-1", "case-1", "import-1", "message-1");

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.CommandText = "DELETE FROM messages WHERE id = $id;";
            deleteCommand.Parameters.AddWithValue("$id", "message-1");
            await deleteCommand.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT linked_message_id FROM attachments WHERE id = $id;";
        command.Parameters.AddWithValue("$id", "attachment-1");
        var linkedMessageId = await command.ExecuteScalarAsync();

        Assert.True(linkedMessageId is DBNull or null);
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

    private static async Task InsertIdentityAsync(
        SqliteConnection connection,
        string identityId,
        string caseId,
        string identityType,
        string rawValue)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO identities (
                id,
                case_id,
                identity_type,
                raw_value,
                display_value,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                $caseId,
                $identityType,
                $rawValue,
                $displayValue,
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", identityId);
        command.Parameters.AddWithValue("$caseId", caseId);
        command.Parameters.AddWithValue("$identityType", identityType);
        command.Parameters.AddWithValue("$rawValue", rawValue);
        command.Parameters.AddWithValue("$displayValue", rawValue);
        command.Parameters.AddWithValue("$createdAtUtc", "2026-01-01T00:00:00Z");
        command.Parameters.AddWithValue("$updatedAtUtc", "2026-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertIdentityLinkAsync(
        SqliteConnection connection,
        string linkId,
        string caseId,
        string sourceIdentityId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO identity_links (
                id,
                case_id,
                source_identity_id,
                link_type,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                $caseId,
                $sourceIdentityId,
                'possible_same_person',
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", linkId);
        command.Parameters.AddWithValue("$caseId", caseId);
        command.Parameters.AddWithValue("$sourceIdentityId", sourceIdentityId);
        command.Parameters.AddWithValue("$createdAtUtc", "2026-01-01T00:00:00Z");
        command.Parameters.AddWithValue("$updatedAtUtc", "2026-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertMessageAsync(
        SqliteConnection connection,
        string messageId,
        string caseId,
        string sourceImportId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO messages (
                id,
                case_id,
                source_import_id,
                event_time_original,
                event_time_utc,
                direction,
                message_body,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                $caseId,
                $sourceImportId,
                '2026-01-01 00:00:00',
                '2026-01-01T00:00:00Z',
                'outbound',
                'synthetic message',
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", messageId);
        command.Parameters.AddWithValue("$caseId", caseId);
        command.Parameters.AddWithValue("$sourceImportId", sourceImportId);
        command.Parameters.AddWithValue("$createdAtUtc", "2026-01-01T00:00:00Z");
        command.Parameters.AddWithValue("$updatedAtUtc", "2026-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertMessageRecipientAsync(
        SqliteConnection connection,
        string recipientId,
        string caseId,
        string messageId,
        string identityId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO message_recipients (
                id,
                case_id,
                message_id,
                recipient_identity_id,
                created_at_utc
            )
            VALUES (
                $id,
                $caseId,
                $messageId,
                $identityId,
                $createdAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", recipientId);
        command.Parameters.AddWithValue("$caseId", caseId);
        command.Parameters.AddWithValue("$messageId", messageId);
        command.Parameters.AddWithValue("$identityId", identityId);
        command.Parameters.AddWithValue("$createdAtUtc", "2026-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertAttachmentAsync(
        SqliteConnection connection,
        string attachmentId,
        string caseId,
        string sourceImportId,
        string messageId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO attachments (
                id,
                case_id,
                source_import_id,
                linked_message_id,
                filename,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                $caseId,
                $sourceImportId,
                $messageId,
                'synthetic.jpg',
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", attachmentId);
        command.Parameters.AddWithValue("$caseId", caseId);
        command.Parameters.AddWithValue("$sourceImportId", sourceImportId);
        command.Parameters.AddWithValue("$messageId", messageId);
        command.Parameters.AddWithValue("$createdAtUtc", "2026-01-01T00:00:00Z");
        command.Parameters.AddWithValue("$updatedAtUtc", "2026-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }
}
