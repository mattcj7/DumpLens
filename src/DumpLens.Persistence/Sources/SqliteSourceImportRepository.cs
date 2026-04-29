using System.Globalization;
using DumpLens.Application.Sources;
using Microsoft.Data.Sqlite;

namespace DumpLens.Persistence.Sources;

public sealed class SqliteSourceImportRepository : ISourceImportRepository
{
    public async Task<bool> CaseExistsAsync(
        string connectionString,
        string caseId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM cases
                WHERE id = $caseId
            );
            """;
        command.Parameters.AddWithValue("$caseId", caseId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long value && value == 1;
    }

    public async Task InsertAsync(
        string connectionString,
        SourceImportRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO source_imports (
                id,
                case_id,
                source_name,
                source_type,
                platform,
                owner_person_id,
                device_id,
                platform_account_id,
                extraction_type,
                provider_return_type,
                original_filename,
                original_file_path,
                stored_file_path,
                file_size_bytes,
                file_sha256,
                file_md5,
                imported_by_user_id,
                imported_at_utc,
                import_status,
                record_count,
                warning_count,
                notes,
                source_metadata_json,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                $caseId,
                $sourceName,
                $sourceType,
                $platform,
                $ownerPersonId,
                $deviceId,
                $platformAccountId,
                $extractionType,
                $providerReturnType,
                $originalFilename,
                $originalFilePath,
                $storedFilePath,
                $fileSizeBytes,
                $fileSha256,
                $fileMd5,
                $importedByUserId,
                $importedAtUtc,
                $importStatus,
                $recordCount,
                $warningCount,
                $notes,
                $sourceMetadataJson,
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$caseId", record.CaseId);
        command.Parameters.AddWithValue("$sourceName", record.SourceName);
        command.Parameters.AddWithValue("$sourceType", record.SourceType);
        command.Parameters.AddWithValue("$platform", ToSqlValue(record.Platform));
        command.Parameters.AddWithValue("$ownerPersonId", ToSqlValue(record.OwnerPersonId));
        command.Parameters.AddWithValue("$deviceId", ToSqlValue(record.DeviceId));
        command.Parameters.AddWithValue("$platformAccountId", ToSqlValue(record.PlatformAccountId));
        command.Parameters.AddWithValue("$extractionType", ToSqlValue(record.ExtractionType));
        command.Parameters.AddWithValue("$providerReturnType", ToSqlValue(record.ProviderReturnType));
        command.Parameters.AddWithValue("$originalFilename", record.OriginalFilename);
        command.Parameters.AddWithValue("$originalFilePath", ToSqlValue(record.OriginalFilePath));
        command.Parameters.AddWithValue("$storedFilePath", ToSqlValue(record.StoredFilePath));
        command.Parameters.AddWithValue("$fileSizeBytes", record.FileSizeBytes.HasValue ? record.FileSizeBytes.Value : DBNull.Value);
        command.Parameters.AddWithValue("$fileSha256", record.FileSha256);
        command.Parameters.AddWithValue("$fileMd5", ToSqlValue(record.FileMd5));
        command.Parameters.AddWithValue("$importedByUserId", ToSqlValue(record.ImportedByUserId));
        command.Parameters.AddWithValue("$importedAtUtc", FormatUtc(record.ImportedAtUtc));
        command.Parameters.AddWithValue("$importStatus", record.ImportStatus);
        command.Parameters.AddWithValue("$recordCount", record.RecordCount);
        command.Parameters.AddWithValue("$warningCount", record.WarningCount);
        command.Parameters.AddWithValue("$notes", ToSqlValue(record.Notes));
        command.Parameters.AddWithValue("$sourceMetadataJson", ToSqlValue(record.SourceMetadataJson));
        command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(record.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(record.UpdatedAtUtc));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnableForeignKeysAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static object ToSqlValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value;
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
