using System.Data;
using System.Globalization;
using DumpLens.Application.Audit;
using DumpLens.Core.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpLens.Persistence.Audit;

public sealed class SqliteAuditLogger : IAuditLogger
{
    private const string WriteOperationName = "audit_event_write";
    private const string VerifyOperationName = "audit_chain_verify";

    private readonly string _connectionString;
    private readonly ILogger<SqliteAuditLogger> _logger;

    public SqliteAuditLogger(string connectionString, ILogger<SqliteAuditLogger>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
        _logger = logger ?? NullLogger<SqliteAuditLogger>.Instance;
    }

    public async Task<AuditEventWriteResult> WriteAsync(
        AuditEventDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var correlationId = NormalizeCorrelationId(draft.CorrelationId);
        var record = CreateRecord(draft);

        _logger.LogInformation(
            "Audit event write started. operation={Operation} correlation_id={CorrelationId} audit_event_id={AuditEventId} case_id={CaseId} user_id={UserId} action_type={ActionType} entity_type={EntityType} entity_id={EntityId} old_value_present={OldValuePresent} new_value_present={NewValuePresent}",
            WriteOperationName,
            correlationId,
            record.Id,
            record.CaseId,
            record.UserId,
            record.ActionType,
            record.EntityType,
            record.EntityId,
            record.OldValueJson is not null,
            record.NewValueJson is not null);

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var previousEvent = await LoadLatestEventAsync(connection, transaction, record.CaseId, cancellationToken).ConfigureAwait(false);
            EnsureAppendOnlyOrdering(previousEvent, record);

            var hashInput = ToHashInput(record);
            var canonicalJson = AuditEventCanonicalizer.CreateCanonicalJson(hashInput);
            var currentHash = AuditChainHash.ComputeHash(previousEvent?.HashChainCurrent, canonicalJson);
            var finalizedRecord = new AuditEventRecord
            {
                Id = record.Id,
                CaseId = record.CaseId,
                UserId = record.UserId,
                ActionType = record.ActionType,
                EntityType = record.EntityType,
                EntityId = record.EntityId,
                Summary = record.Summary,
                OldValueJson = record.OldValueJson,
                NewValueJson = record.NewValueJson,
                Reason = record.Reason,
                EventTimeUtc = record.EventTimeUtc,
                Workstation = record.Workstation,
                AppVersion = record.AppVersion,
                HashChainPrevious = previousEvent?.HashChainCurrent,
                HashChainCurrent = currentHash
            };

            await InsertAuditEventAsync(connection, transaction, finalizedRecord, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Audit event written. operation={Operation} correlation_id={CorrelationId} audit_event_id={AuditEventId} case_id={CaseId} action_type={ActionType} hash_chain_previous_prefix={PreviousHashPrefix} hash_chain_current_prefix={CurrentHashPrefix}",
                WriteOperationName,
                correlationId,
                finalizedRecord.Id,
                finalizedRecord.CaseId,
                finalizedRecord.ActionType,
                GetHashPrefix(finalizedRecord.HashChainPrevious),
                GetHashPrefix(finalizedRecord.HashChainCurrent));

            return new AuditEventWriteResult
            {
                CorrelationId = correlationId,
                AuditEvent = finalizedRecord
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Audit event write failed. operation={Operation} correlation_id={CorrelationId} audit_event_id={AuditEventId} case_id={CaseId} action_type={ActionType} failure_type={FailureType}",
                WriteOperationName,
                correlationId,
                record.Id,
                record.CaseId,
                record.ActionType,
                exception.GetType().Name);
            throw;
        }
    }

    public async Task<AuditChainVerificationResult> VerifyChainAsync(
        string? caseId,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedCaseId = NormalizeOptional(caseId);
        var effectiveCorrelationId = NormalizeCorrelationId(correlationId);

        _logger.LogInformation(
            "Audit chain verification started. operation={Operation} correlation_id={CorrelationId} case_id={CaseId}",
            VerifyOperationName,
            effectiveCorrelationId,
            normalizedCaseId);

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

            var events = await LoadEventsForVerificationAsync(connection, normalizedCaseId, cancellationToken).ConfigureAwait(false);

            string? expectedPreviousHash = null;
            for (var index = 0; index < events.Count; index++)
            {
                var auditEvent = events[index];

                if (!string.Equals(auditEvent.HashChainPrevious, expectedPreviousHash, StringComparison.Ordinal))
                {
                    return LogAndReturnVerificationFailure(
                        effectiveCorrelationId,
                        normalizedCaseId,
                        index + 1,
                        auditEvent.Id,
                        AuditChainFailureCodes.PreviousHashMismatch,
                        "Stored hash_chain_previous does not match the prior audit event hash.");
                }

                var expectedCurrentHash = AuditChainHash.ComputeHash(expectedPreviousHash, AuditEventCanonicalizer.CreateCanonicalJson(ToHashInput(auditEvent)));
                if (!string.Equals(auditEvent.HashChainCurrent, expectedCurrentHash, StringComparison.Ordinal))
                {
                    return LogAndReturnVerificationFailure(
                        effectiveCorrelationId,
                        normalizedCaseId,
                        index + 1,
                        auditEvent.Id,
                        AuditChainFailureCodes.CurrentHashMismatch,
                        "Stored hash_chain_current does not match the recomputed audit chain hash.");
                }

                expectedPreviousHash = auditEvent.HashChainCurrent;
            }

            var validResult = new AuditChainVerificationResult
            {
                CaseId = normalizedCaseId,
                IsValid = true,
                CheckedEventCount = events.Count,
                FailureCode = AuditChainFailureCodes.None,
                Reason = "Audit chain verified."
            };

            _logger.LogInformation(
                "Audit chain verification completed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} checked_event_count={CheckedEventCount} is_valid={IsValid}",
                VerifyOperationName,
                effectiveCorrelationId,
                normalizedCaseId,
                validResult.CheckedEventCount,
                validResult.IsValid);

            return validResult;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Audit chain verification failed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} failure_type={FailureType}",
                VerifyOperationName,
                effectiveCorrelationId,
                normalizedCaseId,
                exception.GetType().Name);
            throw;
        }
    }

    private AuditChainVerificationResult LogAndReturnVerificationFailure(
        string correlationId,
        string? caseId,
        int checkedEventCount,
        string auditEventId,
        string failureCode,
        string reason)
    {
        var result = new AuditChainVerificationResult
        {
            CaseId = caseId,
            IsValid = false,
            CheckedEventCount = checkedEventCount,
            FirstInvalidAuditEventId = auditEventId,
            FailureCode = failureCode,
            Reason = reason
        };

        _logger.LogCritical(
            "Audit chain verification failed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} checked_event_count={CheckedEventCount} first_invalid_audit_event_id={FirstInvalidAuditEventId} failure_code={FailureCode}",
            VerifyOperationName,
            correlationId,
            caseId,
            checkedEventCount,
            auditEventId,
            failureCode);

        return result;
    }

    private static AuditEventRecord CreateRecord(AuditEventDraft draft)
    {
        var id = NormalizeIdentifier(draft.Id) ?? Guid.NewGuid().ToString("N");
        var actionType = NormalizeRequired(draft.ActionType, nameof(draft.ActionType));
        var summary = NormalizeRequired(draft.Summary, nameof(draft.Summary));
        var eventTimeUtc = (draft.EventTimeUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();

        return new AuditEventRecord
        {
            Id = id,
            CaseId = NormalizeOptional(draft.CaseId),
            UserId = NormalizeOptional(draft.UserId),
            ActionType = actionType,
            EntityType = NormalizeOptional(draft.EntityType),
            EntityId = NormalizeOptional(draft.EntityId),
            Summary = summary,
            OldValueJson = NormalizeOptionalJson(draft.OldValueJson),
            NewValueJson = NormalizeOptionalJson(draft.NewValueJson),
            Reason = NormalizeOptional(draft.Reason),
            EventTimeUtc = eventTimeUtc,
            Workstation = NormalizeOptional(draft.Workstation),
            AppVersion = NormalizeOptional(draft.AppVersion),
            HashChainCurrent = string.Empty
        };
    }

    private static void EnsureAppendOnlyOrdering(AuditEventRecord? previousEvent, AuditEventRecord currentEvent)
    {
        if (previousEvent is null)
        {
            return;
        }

        var eventTimeComparison = DateTimeOffset.Compare(currentEvent.EventTimeUtc, previousEvent.EventTimeUtc);
        if (eventTimeComparison < 0 ||
            (eventTimeComparison == 0 && string.CompareOrdinal(currentEvent.Id, previousEvent.Id) <= 0))
        {
            throw new InvalidOperationException("Audit events must be appended in event_time_utc/id order within each case.");
        }
    }

    private static AuditEventHashInput ToHashInput(AuditEventRecord record)
    {
        return new AuditEventHashInput(
            record.Id,
            record.CaseId,
            record.UserId,
            record.ActionType,
            record.EntityType,
            record.EntityId,
            record.Summary,
            record.OldValueJson,
            record.NewValueJson,
            record.Reason,
            record.EventTimeUtc,
            record.Workstation,
            record.AppVersion);
    }

    private static async Task EnableForeignKeysAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AuditEventRecord?> LoadLatestEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? caseId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                id,
                case_id,
                user_id,
                action_type,
                entity_type,
                entity_id,
                summary,
                old_value_json,
                new_value_json,
                reason,
                event_time_utc,
                workstation,
                app_version,
                hash_chain_previous,
                hash_chain_current
            FROM audit_events
            WHERE (($caseId IS NULL AND case_id IS NULL) OR case_id = $caseId)
            ORDER BY event_time_utc DESC, id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$caseId", (object?)caseId ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadAuditEvent(reader)
            : null;
    }

    private static async Task<IReadOnlyList<AuditEventRecord>> LoadEventsForVerificationAsync(
        SqliteConnection connection,
        string? caseId,
        CancellationToken cancellationToken)
    {
        var events = new List<AuditEventRecord>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                case_id,
                user_id,
                action_type,
                entity_type,
                entity_id,
                summary,
                old_value_json,
                new_value_json,
                reason,
                event_time_utc,
                workstation,
                app_version,
                hash_chain_previous,
                hash_chain_current
            FROM audit_events
            WHERE (($caseId IS NULL AND case_id IS NULL) OR case_id = $caseId)
            ORDER BY event_time_utc ASC, id ASC;
            """;
        command.Parameters.AddWithValue("$caseId", (object?)caseId ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(ReadAuditEvent(reader));
        }

        return events;
    }

    private static async Task InsertAuditEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AuditEventRecord record,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO audit_events (
                id,
                case_id,
                user_id,
                action_type,
                entity_type,
                entity_id,
                summary,
                old_value_json,
                new_value_json,
                reason,
                event_time_utc,
                workstation,
                app_version,
                hash_chain_previous,
                hash_chain_current
            )
            VALUES (
                $id,
                $caseId,
                $userId,
                $actionType,
                $entityType,
                $entityId,
                $summary,
                $oldValueJson,
                $newValueJson,
                $reason,
                $eventTimeUtc,
                $workstation,
                $appVersion,
                $hashChainPrevious,
                $hashChainCurrent
            );
            """;
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$caseId", (object?)record.CaseId ?? DBNull.Value);
        command.Parameters.AddWithValue("$userId", (object?)record.UserId ?? DBNull.Value);
        command.Parameters.AddWithValue("$actionType", record.ActionType);
        command.Parameters.AddWithValue("$entityType", (object?)record.EntityType ?? DBNull.Value);
        command.Parameters.AddWithValue("$entityId", (object?)record.EntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("$summary", record.Summary);
        command.Parameters.AddWithValue("$oldValueJson", (object?)record.OldValueJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$newValueJson", (object?)record.NewValueJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$reason", (object?)record.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$eventTimeUtc", record.EventTimeUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$workstation", (object?)record.Workstation ?? DBNull.Value);
        command.Parameters.AddWithValue("$appVersion", (object?)record.AppVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$hashChainPrevious", (object?)record.HashChainPrevious ?? DBNull.Value);
        command.Parameters.AddWithValue("$hashChainCurrent", record.HashChainCurrent);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AuditEventRecord ReadAuditEvent(SqliteDataReader reader)
    {
        return new AuditEventRecord
        {
            Id = reader.GetString(0),
            CaseId = reader.IsDBNull(1) ? null : reader.GetString(1),
            UserId = reader.IsDBNull(2) ? null : reader.GetString(2),
            ActionType = reader.GetString(3),
            EntityType = reader.IsDBNull(4) ? null : reader.GetString(4),
            EntityId = reader.IsDBNull(5) ? null : reader.GetString(5),
            Summary = reader.GetString(6),
            OldValueJson = reader.IsDBNull(7) ? null : reader.GetString(7),
            NewValueJson = reader.IsDBNull(8) ? null : reader.GetString(8),
            Reason = reader.IsDBNull(9) ? null : reader.GetString(9),
            EventTimeUtc = DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
            Workstation = reader.IsDBNull(11) ? null : reader.GetString(11),
            AppVersion = reader.IsDBNull(12) ? null : reader.GetString(12),
            HashChainPrevious = reader.IsDBNull(13) ? null : reader.GetString(13),
            HashChainCurrent = reader.GetString(14)
        };
    }

    private static string NormalizeCorrelationId(string? correlationId)
    {
        return NormalizeOptional(correlationId) ?? Guid.NewGuid().ToString("N");
    }

    private static string? NormalizeIdentifier(string? value)
    {
        return NormalizeOptional(value);
    }

    private static string NormalizeRequired(string? value, string paramName)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            throw new ArgumentException("A non-empty value is required.", paramName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string? NormalizeOptionalJson(string? value)
    {
        return NormalizeOptional(value);
    }

    private static string? GetHashPrefix(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        return hash.Length <= 12
            ? hash
            : hash[..12];
    }
}
