using System.Globalization;
using DumpLens.Application.Audit;
using DumpLens.Persistence.Audit;
using DumpLens.Persistence.Database;
using DumpLens.Tests.Integration.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DumpLens.Tests.Integration.Audit;

public sealed class SqliteAuditLoggerTests
{
    [Fact]
    public async Task WriteAsync_StoresAuditEventsAndBuildsContinuousHashChain()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        await MigrateDatabaseAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();
        await InsertCaseAsync(connection, "case-1");
        await InsertUserAsync(connection, "user-1");

        var logger = new SqliteAuditLogger(tempDatabase.ConnectionString);

        var firstWrite = await logger.WriteAsync(new AuditEventDraft
        {
            Id = "audit-1",
            CaseId = "case-1",
            UserId = "user-1",
            ActionType = "case_create",
            EntityType = "case",
            EntityId = "case-1",
            Summary = "Created synthetic case",
            NewValueJson = """{"status":"open","title":"Synthetic Case"}""",
            Reason = "synthetic setup",
            EventTimeUtc = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
            Workstation = "LAB-01",
            AppVersion = "1.2.3",
            CorrelationId = "audit-write-1"
        });

        var secondWrite = await logger.WriteAsync(new AuditEventDraft
        {
            Id = "audit-2",
            CaseId = "case-1",
            UserId = "user-1",
            ActionType = "case_update",
            EntityType = "case",
            EntityId = "case-1",
            Summary = "Updated synthetic case title",
            OldValueJson = """{"title":"Synthetic Case"}""",
            NewValueJson = """{"title":"Synthetic Case Revised"}""",
            Reason = "synthetic update",
            EventTimeUtc = new DateTimeOffset(2026, 3, 1, 12, 5, 0, TimeSpan.Zero),
            Workstation = "LAB-01",
            AppVersion = "1.2.3",
            CorrelationId = "audit-write-2"
        });

        var storedEvents = await LoadAuditEventsAsync(connection, "case-1");
        Assert.Equal(2, storedEvents.Count);
        Assert.Null(storedEvents[0].HashChainPrevious);
        Assert.False(string.IsNullOrWhiteSpace(storedEvents[0].HashChainCurrent));
        Assert.Equal(storedEvents[0].HashChainCurrent, storedEvents[1].HashChainPrevious);
        Assert.Equal(firstWrite.AuditEvent.HashChainCurrent, storedEvents[0].HashChainCurrent);
        Assert.Equal(secondWrite.AuditEvent.HashChainCurrent, storedEvents[1].HashChainCurrent);

        var verification = await logger.VerifyChainAsync("case-1", correlationId: "audit-verify-1");
        Assert.True(verification.IsValid);
        Assert.Equal(2, verification.CheckedEventCount);
        Assert.Equal(AuditChainFailureCodes.None, verification.FailureCode);
        Assert.Null(verification.FirstInvalidAuditEventId);
    }

    [Fact]
    public async Task VerifyChainAsync_DetectsTamperingAfterStoredRowIsModified()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        await MigrateDatabaseAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();
        await InsertCaseAsync(connection, "case-1");
        await InsertUserAsync(connection, "user-1");

        var logger = new SqliteAuditLogger(tempDatabase.ConnectionString);

        await logger.WriteAsync(new AuditEventDraft
        {
            Id = "audit-1",
            CaseId = "case-1",
            UserId = "user-1",
            ActionType = "case_create",
            EntityType = "case",
            EntityId = "case-1",
            Summary = "Created synthetic case",
            NewValueJson = """{"status":"open"}""",
            EventTimeUtc = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero)
        });

        await logger.WriteAsync(new AuditEventDraft
        {
            Id = "audit-2",
            CaseId = "case-1",
            UserId = "user-1",
            ActionType = "case_update",
            EntityType = "case",
            EntityId = "case-1",
            Summary = "Updated synthetic case",
            OldValueJson = """{"status":"open"}""",
            NewValueJson = """{"status":"review"}""",
            EventTimeUtc = new DateTimeOffset(2026, 3, 1, 12, 2, 0, TimeSpan.Zero)
        });

        await using (var tamperCommand = connection.CreateCommand())
        {
            tamperCommand.CommandText =
                """
                UPDATE audit_events
                SET new_value_json = $newValueJson
                WHERE id = $id;
                """;
            tamperCommand.Parameters.AddWithValue("$id", "audit-2");
            tamperCommand.Parameters.AddWithValue("$newValueJson", """{"status":"tampered"}""");
            await tamperCommand.ExecuteNonQueryAsync();
        }

        var verification = await logger.VerifyChainAsync("case-1", correlationId: "audit-verify-tamper");

        Assert.False(verification.IsValid);
        Assert.Equal(2, verification.CheckedEventCount);
        Assert.Equal("audit-2", verification.FirstInvalidAuditEventId);
        Assert.Equal(AuditChainFailureCodes.CurrentHashMismatch, verification.FailureCode);
    }

    [Fact]
    public async Task AuditOperations_EmitEvidenceSafeStructuredLogs()
    {
        using var tempDatabase = TemporarySqliteDatabase.Create();
        await MigrateDatabaseAsync(tempDatabase.ConnectionString);

        await using var connection = new SqliteConnection(tempDatabase.ConnectionString);
        await connection.OpenAsync();
        await InsertCaseAsync(connection, "case-1");
        await InsertUserAsync(connection, "user-1");

        var testLogger = new TestLogger<SqliteAuditLogger>();
        var logger = new SqliteAuditLogger(tempDatabase.ConnectionString, testLogger);
        const string sensitiveJsonToken = "TOP_SECRET_AUDIT_JSON";

        await logger.WriteAsync(new AuditEventDraft
        {
            Id = "audit-1",
            CaseId = "case-1",
            UserId = "user-1",
            ActionType = "case_create",
            EntityType = "case",
            EntityId = "case-1",
            Summary = "Created synthetic case",
            OldValueJson = $"{{\"before\":\"{sensitiveJsonToken}\"}}",
            NewValueJson = $"{{\"after\":\"{sensitiveJsonToken}\"}}",
            EventTimeUtc = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
            CorrelationId = "audit-safe-log-write"
        });

        await logger.VerifyChainAsync("case-1", correlationId: "audit-safe-log-verify");

        await using (var tamperCommand = connection.CreateCommand())
        {
            tamperCommand.CommandText = "UPDATE audit_events SET summary = $summary WHERE id = $id;";
            tamperCommand.Parameters.AddWithValue("$id", "audit-1");
            tamperCommand.Parameters.AddWithValue("$summary", "Tampered synthetic summary");
            await tamperCommand.ExecuteNonQueryAsync();
        }

        await logger.VerifyChainAsync("case-1", correlationId: "audit-safe-log-verify-failed");

        await Assert.ThrowsAsync<SqliteException>(() => logger.WriteAsync(new AuditEventDraft
        {
            Id = "audit-missing-case",
            CaseId = "missing-case",
            UserId = "user-1",
            ActionType = "case_update",
            EntityType = "case",
            EntityId = "missing-case",
            Summary = "Should fail synthetic write",
            NewValueJson = $"{{\"after\":\"{sensitiveJsonToken}\"}}",
            EventTimeUtc = new DateTimeOffset(2026, 3, 1, 12, 10, 0, TimeSpan.Zero),
            CorrelationId = "audit-safe-log-write-failed"
        }));

        Assert.Contains(testLogger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Audit event write started.", StringComparison.Ordinal));
        Assert.Contains(testLogger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Audit event written.", StringComparison.Ordinal));
        Assert.Contains(testLogger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Audit chain verification started.", StringComparison.Ordinal));
        Assert.Contains(testLogger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Audit chain verification completed.", StringComparison.Ordinal));
        Assert.Contains(testLogger.Entries, entry => entry.Level == LogLevel.Critical && entry.Message.Contains("Audit chain verification failed.", StringComparison.Ordinal));
        Assert.Contains(testLogger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Audit event write failed.", StringComparison.Ordinal));
        Assert.All(testLogger.Entries, entry => Assert.DoesNotContain(sensitiveJsonToken, entry.Message, StringComparison.Ordinal));
        Assert.All(
            testLogger.Entries,
            entry =>
            {
                Assert.True(entry.State.ContainsKey("Operation"));
                Assert.True(entry.State.ContainsKey("CorrelationId"));
            });
    }

    private static async Task MigrateDatabaseAsync(string connectionString)
    {
        var runner = new SqliteMigrationRunner();
        await runner.RunMigrationsAsync(connectionString);
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
                'DL-001',
                'Synthetic Case',
                'open',
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", caseId);
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
                'Synthetic Investigator',
                'synthetic.user',
                'investigator',
                1,
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", userId);
        command.Parameters.AddWithValue("$createdAtUtc", "2026-01-01T00:00:00Z");
        command.Parameters.AddWithValue("$updatedAtUtc", "2026-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<AuditEventRecord>> LoadAuditEventsAsync(SqliteConnection connection, string caseId)
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
            WHERE case_id = $caseId
            ORDER BY event_time_utc ASC, id ASC;
            """;
        command.Parameters.AddWithValue("$caseId", caseId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new AuditEventRecord
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
                EventTimeUtc = DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Workstation = reader.IsDBNull(11) ? null : reader.GetString(11),
                AppVersion = reader.IsDBNull(12) ? null : reader.GetString(12),
                HashChainPrevious = reader.IsDBNull(13) ? null : reader.GetString(13),
                HashChainCurrent = reader.GetString(14)
            });
        }

        return events;
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NoOpScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var structuredState = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (state is IEnumerable<KeyValuePair<string, object?>> keyValuePairs)
            {
                foreach (var keyValuePair in keyValuePairs)
                {
                    if (keyValuePair.Key == "{OriginalFormat}")
                    {
                        continue;
                    }

                    structuredState[keyValuePair.Key] = keyValuePair.Value;
                }
            }

            Entries.Add(new LogEntry(logLevel, formatter(state, exception), structuredState, exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> State,
        Exception? Exception);

    private sealed class NoOpScope : IDisposable
    {
        public static NoOpScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
