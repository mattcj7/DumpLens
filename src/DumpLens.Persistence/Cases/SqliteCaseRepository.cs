using System.Globalization;
using DumpLens.Application.Cases;
using Microsoft.Data.Sqlite;

namespace DumpLens.Persistence.Cases;

public sealed class SqliteCaseRepository : ICaseRepository
{
    public async Task<CaseSummary> InsertAsync(
        string connectionString,
        CaseRecord record,
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
            INSERT INTO cases (
                id,
                case_number,
                title,
                incident_type,
                incident_start_utc,
                incident_end_utc,
                incident_timezone,
                incident_location_text,
                lead_investigator,
                agency,
                summary,
                case_status,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                $caseNumber,
                $title,
                $incidentType,
                $incidentStartUtc,
                $incidentEndUtc,
                $incidentTimezone,
                $incidentLocationText,
                $leadInvestigator,
                $agency,
                $summary,
                $caseStatus,
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$caseNumber", (object?)record.CaseNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$incidentType", (object?)record.IncidentType ?? DBNull.Value);
        command.Parameters.AddWithValue("$incidentStartUtc", ToSqlValue(record.IncidentStartUtc));
        command.Parameters.AddWithValue("$incidentEndUtc", ToSqlValue(record.IncidentEndUtc));
        command.Parameters.AddWithValue("$incidentTimezone", (object?)record.IncidentTimezone ?? DBNull.Value);
        command.Parameters.AddWithValue("$incidentLocationText", (object?)record.IncidentLocationText ?? DBNull.Value);
        command.Parameters.AddWithValue("$leadInvestigator", (object?)record.LeadInvestigator ?? DBNull.Value);
        command.Parameters.AddWithValue("$agency", (object?)record.Agency ?? DBNull.Value);
        command.Parameters.AddWithValue("$summary", (object?)record.Summary ?? DBNull.Value);
        command.Parameters.AddWithValue("$caseStatus", record.CaseStatus);
        command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(record.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(record.UpdatedAtUtc));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return new CaseSummary
        {
            CaseId = record.Id,
            CaseNumber = record.CaseNumber,
            Title = record.Title,
            CaseStatus = record.CaseStatus,
            CreatedAtUtc = record.CreatedAtUtc.ToUniversalTime()
        };
    }

    private static async Task EnableForeignKeysAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static object ToSqlValue(DateTimeOffset? value)
    {
        return value.HasValue
            ? FormatUtc(value.Value)
            : DBNull.Value;
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
