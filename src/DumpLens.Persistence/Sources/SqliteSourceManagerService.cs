using System.Globalization;
using DumpLens.Application.Sources;
using Microsoft.Data.Sqlite;

namespace DumpLens.Persistence.Sources;

public sealed class SqliteSourceManagerService : ISourceManagerService
{
    public async Task<IReadOnlyList<SourceImportSummary>> GetSummariesAsync(
        LoadSourceImportSummariesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = Normalize(request);
        var results = new List<SourceImportSummary>();

        await using var connection = new SqliteConnection(BuildConnectionString(normalizedRequest.CaseDatabasePath));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                source_name,
                source_type,
                platform,
                import_status,
                record_count,
                warning_count,
                imported_at_utc,
                original_filename,
                file_size_bytes,
                file_sha256
            FROM source_imports
            WHERE case_id = $caseId
            ORDER BY imported_at_utc DESC, source_name COLLATE NOCASE ASC, id ASC;
            """;
        command.Parameters.AddWithValue("$caseId", normalizedRequest.CaseId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new SourceImportSummary
            {
                SourceImportId = reader.GetString(0),
                SourceName = reader.GetString(1),
                SourceType = reader.GetString(2),
                Platform = reader.IsDBNull(3) ? null : reader.GetString(3),
                ImportStatus = reader.GetString(4),
                RecordCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                WarningCount = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                ImportedAtUtc = ParseUtc(reader.GetString(7)),
                OriginalFilename = reader.GetString(8),
                FileSizeBytes = reader.IsDBNull(9) ? null : reader.GetInt64(9),
                FileSha256 = reader.GetString(10)
            });
        }

        return results;
    }

    public async Task<SourceImportDetail?> GetDetailAsync(
        LoadSourceImportDetailRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = Normalize(request);

        await using var connection = new SqliteConnection(BuildConnectionString(normalizedRequest.CaseDatabasePath));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                source_name,
                source_type,
                platform,
                original_filename,
                stored_file_path,
                file_size_bytes,
                file_sha256,
                imported_at_utc,
                imported_by_user_id,
                import_status,
                record_count,
                warning_count,
                notes,
                source_metadata_json
            FROM source_imports
            WHERE case_id = $caseId AND id = $sourceImportId;
            """;
        command.Parameters.AddWithValue("$caseId", normalizedRequest.CaseId);
        command.Parameters.AddWithValue("$sourceImportId", normalizedRequest.SourceImportId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var warningCount = reader.IsDBNull(12) ? 0 : reader.GetInt32(12);

        return new SourceImportDetail
        {
            SourceImportId = reader.GetString(0),
            SourceName = reader.GetString(1),
            SourceType = reader.GetString(2),
            Platform = reader.IsDBNull(3) ? null : reader.GetString(3),
            OriginalFilename = reader.GetString(4),
            StoredFilePath = NormalizeStoredFilePath(
                reader.IsDBNull(5) ? null : reader.GetString(5),
                normalizedRequest.CasePackageRootPath),
            FileSizeBytes = reader.IsDBNull(6) ? null : reader.GetInt64(6),
            FileSha256 = reader.GetString(7),
            ImportedAtUtc = ParseUtc(reader.GetString(8)),
            ImportedByUserId = reader.IsDBNull(9) ? null : reader.GetString(9),
            ImportStatus = reader.GetString(10),
            RecordCount = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
            WarningCount = warningCount,
            HasNotes = !reader.IsDBNull(13) && !string.IsNullOrWhiteSpace(reader.GetString(13)),
            HasSourceMetadata = !reader.IsDBNull(14) && !string.IsNullOrWhiteSpace(reader.GetString(14)),
            WarningSummary = new SourceWarningSummary
            {
                TotalWarnings = warningCount,
                WarningCodeCounts = await LoadWarningCodesAsync(
                    connection,
                    normalizedRequest.SourceImportId!,
                    cancellationToken).ConfigureAwait(false)
            }
        };
    }

    private static async Task<IReadOnlyList<SourceWarningCodeCount>> LoadWarningCodesAsync(
        SqliteConnection connection,
        string sourceImportId,
        CancellationToken cancellationToken)
    {
        var results = new List<SourceWarningCodeCount>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                warning_code,
                COUNT(*) AS warning_count
            FROM import_warnings
            WHERE source_import_id = $sourceImportId
            GROUP BY warning_code
            ORDER BY warning_count DESC, warning_code ASC;
            """;
        command.Parameters.AddWithValue("$sourceImportId", sourceImportId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new SourceWarningCodeCount
            {
                WarningCode = reader.GetString(0),
                Count = reader.GetInt32(1)
            });
        }

        return results;
    }

    private static string BuildConnectionString(string caseDatabasePath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = caseDatabasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    private static async Task EnableForeignKeysAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset ParseUtc(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ToUniversalTime();
    }

    private static NormalizedRequest Normalize(LoadSourceImportSummariesRequest request)
    {
        return new NormalizedRequest(
            NormalizeRequired(request.CaseId, nameof(request.CaseId)),
            NormalizeAbsoluteFilePath(request.CaseDatabasePath, nameof(request.CaseDatabasePath)),
            NormalizeAbsoluteDirectoryPath(request.CasePackageRootPath, nameof(request.CasePackageRootPath)),
            SourceImportId: null);
    }

    private static NormalizedRequest Normalize(LoadSourceImportDetailRequest request)
    {
        return new NormalizedRequest(
            NormalizeRequired(request.CaseId, nameof(request.CaseId)),
            NormalizeAbsoluteFilePath(request.CaseDatabasePath, nameof(request.CaseDatabasePath)),
            NormalizeAbsoluteDirectoryPath(request.CasePackageRootPath, nameof(request.CasePackageRootPath)),
            NormalizeRequired(request.SourceImportId, nameof(request.SourceImportId)));
    }

    private static string NormalizeAbsoluteFilePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        if (!Path.IsPathRooted(path))
        {
            throw new ArgumentException("The path must be absolute.", parameterName);
        }

        var fullPath = Path.GetFullPath(path.Trim());
        if (!File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("The case database path must exist and point to a file.", fullPath);
        }

        return fullPath;
    }

    private static string NormalizeAbsoluteDirectoryPath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        if (!Path.IsPathRooted(path))
        {
            throw new ArgumentException("The path must be absolute.", parameterName);
        }

        return Path.GetFullPath(path.Trim());
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeStoredFilePath(string? storedFilePath, string casePackageRootPath)
    {
        if (string.IsNullOrWhiteSpace(storedFilePath))
        {
            return null;
        }

        var trimmedPath = storedFilePath.Trim();
        if (!Path.IsPathRooted(trimmedPath))
        {
            return trimmedPath.Replace('\\', '/');
        }

        var fullPath = Path.GetFullPath(trimmedPath);
        var packageRoot = Path.GetFullPath(casePackageRootPath);
        var isWithinCaseRoot = fullPath.StartsWith(
            packageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

        if (isWithinCaseRoot)
        {
            return Path.GetRelativePath(packageRoot, fullPath).Replace('\\', '/');
        }

        return Path.GetFileName(fullPath);
    }

    private sealed record NormalizedRequest(
        string CaseId,
        string CaseDatabasePath,
        string CasePackageRootPath,
        string? SourceImportId);
}
