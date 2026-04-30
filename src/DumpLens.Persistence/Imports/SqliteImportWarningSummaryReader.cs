using DumpLens.Application.Imports;
using Microsoft.Data.Sqlite;

namespace DumpLens.Persistence.Imports;

public sealed class SqliteImportWarningSummaryReader : IImportWarningSummaryReader
{
    public async Task<IReadOnlyList<ImportWarningSummary>> GetSummariesAsync(
        string caseDatabasePath,
        string sourceImportId,
        CancellationToken cancellationToken = default)
    {
        var normalizedCaseDatabasePath = NormalizeAbsoluteFilePath(caseDatabasePath, nameof(caseDatabasePath));
        var normalizedSourceImportId = NormalizeRequired(sourceImportId, nameof(sourceImportId));

        if (!File.Exists(normalizedCaseDatabasePath) || Directory.Exists(normalizedCaseDatabasePath))
        {
            throw new FileNotFoundException("The case database path must exist and point to a file.", normalizedCaseDatabasePath);
        }

        var results = new List<ImportWarningSummary>();
        await using var connection = new SqliteConnection(BuildConnectionString(normalizedCaseDatabasePath));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                warning_code,
                message,
                COUNT(*) AS warning_count
            FROM import_warnings
            WHERE source_import_id = $sourceImportId
            GROUP BY warning_code, message
            ORDER BY warning_count DESC, warning_code ASC, message ASC;
            """;
        command.Parameters.AddWithValue("$sourceImportId", normalizedSourceImportId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ImportWarningSummary
            {
                WarningCode = reader.GetString(0),
                Message = reader.GetString(1),
                Count = reader.GetInt32(2)
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
}
