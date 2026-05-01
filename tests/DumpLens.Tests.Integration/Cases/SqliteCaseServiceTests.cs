using System.Globalization;
using System.Text.Json;
using DumpLens.Application.Audit;
using DumpLens.Application.CasePackages;
using DumpLens.Application.Cases;
using DumpLens.Persistence.Audit;
using DumpLens.Persistence.CasePackages;
using DumpLens.Persistence.Cases;
using DumpLens.Persistence.Database;
using DumpLens.Tests.Integration.CasePackages;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DumpLens.Tests.Integration.Cases;

public sealed class SqliteCaseServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesCasePackageDatabaseCaseRecordAndAuditEvent()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var service = CreateService();

        var result = await service.CreateAsync(new CreateCaseRequest
        {
            CaseNumber = "DL-SYN-001",
            Title = "Synthetic Communications Review",
            IncidentType = "synthetic_incident",
            IncidentStartUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            IncidentEndUtc = new DateTimeOffset(2026, 1, 3, 3, 4, 5, TimeSpan.Zero),
            IncidentTimezone = "UTC",
            IncidentLocationText = "Synthetic Test Location",
            LeadInvestigator = "Synthetic Investigator",
            Agency = "Synthetic Agency",
            Summary = "Synthetic case creation summary.",
            ParentDirectoryPath = tempDirectory.DirectoryPath,
            CreatedByDisplayName = "Synthetic Creator",
            CorrelationId = "case-create-e2e"
        });

        Assert.True(Guid.TryParseExact(result.CaseId, "N", out _));
        Assert.True(Guid.TryParseExact(result.PackageId, "N", out _));
        Assert.Equal("DL-SYN-001", result.CaseNumber);
        Assert.Equal("Synthetic Communications Review", result.Title);
        Assert.Equal("case-create-e2e", result.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(result.AuditEventId));
        Assert.True(Directory.Exists(result.PackageRootPath));
        Assert.True(File.Exists(result.ManifestPath));
        Assert.True(File.Exists(result.DatabasePath));
        Assert.Equal(Path.Combine(result.PackageRootPath, "case.dlensdb"), result.DatabasePath);

        await using (var manifestStream = File.OpenRead(result.ManifestPath))
        {
            var manifest = await JsonSerializer.DeserializeAsync<CasePackageManifest>(manifestStream);

            Assert.NotNull(manifest);
            Assert.Equal(result.PackageId, manifest.PackageId);
            Assert.Equal(result.CaseId, manifest.CaseId);
            Assert.Equal("DL-SYN-001", manifest.CaseNumber);
            Assert.Equal("Synthetic Communications Review", manifest.Title);
            Assert.Equal("case.dlensdb", manifest.DatabaseRelativePath);
        }

        await using var connection = new SqliteConnection(BuildConnectionString(result.DatabasePath));
        await connection.OpenAsync();

        Assert.Equal(
            new[]
            {
                ("0001", "bootstrap_schema_migrations_support"),
                ("0002", "initial_core_schema"),
                ("0003", "communication_schema"),
                ("0004", "message_search_index")
            },
            await LoadAppliedMigrationsAsync(connection));

        var storedCase = await LoadCaseAsync(connection, result.CaseId);
        Assert.NotNull(storedCase);
        Assert.Equal("DL-SYN-001", storedCase.CaseNumber);
        Assert.Equal("Synthetic Communications Review", storedCase.Title);
        Assert.Equal("synthetic_incident", storedCase.IncidentType);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), storedCase.IncidentStartUtc);
        Assert.Equal(new DateTimeOffset(2026, 1, 3, 3, 4, 5, TimeSpan.Zero), storedCase.IncidentEndUtc);
        Assert.Equal("UTC", storedCase.IncidentTimezone);
        Assert.Equal("Synthetic Test Location", storedCase.IncidentLocationText);
        Assert.Equal("Synthetic Investigator", storedCase.LeadInvestigator);
        Assert.Equal("Synthetic Agency", storedCase.Agency);
        Assert.Equal("Synthetic case creation summary.", storedCase.Summary);
        Assert.Equal("open", storedCase.CaseStatus);
        Assert.Equal(TimeSpan.Zero, storedCase.CreatedAtUtc.Offset);
        Assert.Equal(storedCase.CreatedAtUtc, storedCase.UpdatedAtUtc);

        var auditEvent = await LoadSingleAuditEventAsync(connection, result.CaseId);
        Assert.NotNull(auditEvent);
        Assert.Equal(result.AuditEventId, auditEvent.Id);
        Assert.Equal("case_created", auditEvent.ActionType);
        Assert.Equal("case", auditEvent.EntityType);
        Assert.Equal(result.CaseId, auditEvent.EntityId);
        Assert.Equal("Case created.", auditEvent.Summary);
        Assert.Contains(result.PackageId, auditEvent.NewValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic case creation summary.", auditEvent.NewValueJson, StringComparison.Ordinal);

        var auditLogger = new SqliteAuditLogger(BuildConnectionString(result.DatabasePath));
        var verification = await auditLogger.VerifyChainAsync(result.CaseId, result.CorrelationId);

        Assert.True(verification.IsValid);
        Assert.Equal(1, verification.CheckedEventCount);
        Assert.Equal(AuditChainFailureCodes.None, verification.FailureCode);
    }

    [Fact]
    public async Task CreateAsync_FailsValidationForMissingTitleInvalidDirectoryAndTraversalWithoutCreatingPackages()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateCaseRequest
        {
            ParentDirectoryPath = tempDirectory.DirectoryPath,
            Title = " "
        }));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.CreateAsync(new CreateCaseRequest
        {
            ParentDirectoryPath = Path.Combine(tempDirectory.DirectoryPath, "missing-parent"),
            Title = "Synthetic Case"
        }));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateCaseRequest
        {
            ParentDirectoryPath = tempDirectory.DirectoryPath,
            RequestedPackageFolderName = "..\\outside",
            Title = "Synthetic Case"
        }));

        Assert.Empty(Directory.GetDirectories(tempDirectory.DirectoryPath));
    }

    [Fact]
    public async Task CreateAsync_FailsWhenIncidentEndIsBeforeStartWithoutCreatingPackage()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateCaseRequest
        {
            ParentDirectoryPath = tempDirectory.DirectoryPath,
            Title = "Synthetic Case",
            IncidentStartUtc = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero),
            IncidentEndUtc = new DateTimeOffset(2026, 2, 1, 11, 59, 0, TimeSpan.Zero)
        }));

        Assert.Contains("Incident end UTC", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(tempDirectory.DirectoryPath));
    }

    [Fact]
    public async Task CreateAsync_EmitsEvidenceSafeStructuredLogsForSuccessAndFailure()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var logger = new TestLogger<SqliteCaseService>();
        var service = CreateService(logger);
        const string sensitiveToken = "TOP_SECRET_EVIDENCE_CONTENT";

        await service.CreateAsync(new CreateCaseRequest
        {
            CaseNumber = "DL-SYN-LOG",
            Title = sensitiveToken,
            ParentDirectoryPath = tempDirectory.DirectoryPath,
            RequestedPackageFolderName = "safe-log-case",
            CorrelationId = "case-create-log-success"
        });

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateCaseRequest
        {
            Title = sensitiveToken,
            ParentDirectoryPath = tempDirectory.DirectoryPath,
            IncidentStartUtc = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero),
            IncidentEndUtc = new DateTimeOffset(2026, 2, 1, 11, 0, 0, TimeSpan.Zero),
            CorrelationId = "case-create-log-failure"
        }));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Case creation started.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Case package created.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Case database migrated.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Case record inserted.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Case creation audit event written.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Case creation completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Case creation failed.", StringComparison.Ordinal));
        Assert.All(logger.Entries, entry => Assert.DoesNotContain(sensitiveToken, entry.Message, StringComparison.Ordinal));
        Assert.All(
            logger.Entries,
            entry =>
            {
                Assert.True(entry.State.ContainsKey("Operation"));
                Assert.True(entry.State.ContainsKey("CorrelationId"));
                Assert.True(entry.State.ContainsKey("CaseId"));
            });
    }

    private static SqliteCaseService CreateService(TestLogger<SqliteCaseService>? logger = null)
    {
        return new SqliteCaseService(
            new CasePackageService(),
            new SqliteMigrationRunner(),
            new SqliteCaseRepository(),
            logger: logger);
    }

    private static string BuildConnectionString(string databasePath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    private static async Task<List<(string Version, string Name)>> LoadAppliedMigrationsAsync(SqliteConnection connection)
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

    private static async Task<StoredCase?> LoadCaseAsync(SqliteConnection connection, string caseId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
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
            FROM cases
            WHERE id = $caseId;
            """;
        command.Parameters.AddWithValue("$caseId", caseId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new StoredCase(
            ReadNullableString(reader, 0),
            reader.GetString(1),
            ReadNullableString(reader, 2),
            ReadNullableDateTimeOffset(reader, 3),
            ReadNullableDateTimeOffset(reader, 4),
            ReadNullableString(reader, 5),
            ReadNullableString(reader, 6),
            ReadNullableString(reader, 7),
            ReadNullableString(reader, 8),
            ReadNullableString(reader, 9),
            reader.GetString(10),
            DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
            DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime());
    }

    private static async Task<StoredAuditEvent?> LoadSingleAuditEventAsync(SqliteConnection connection, string caseId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, action_type, entity_type, entity_id, summary, new_value_json
            FROM audit_events
            WHERE case_id = $caseId;
            """;
        command.Parameters.AddWithValue("$caseId", caseId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var auditEvent = new StoredAuditEvent(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5));

        Assert.False(await reader.ReadAsync());
        return auditEvent;
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
    }

    private sealed record StoredCase(
        string? CaseNumber,
        string Title,
        string? IncidentType,
        DateTimeOffset? IncidentStartUtc,
        DateTimeOffset? IncidentEndUtc,
        string? IncidentTimezone,
        string? IncidentLocationText,
        string? LeadInvestigator,
        string? Agency,
        string? Summary,
        string CaseStatus,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record StoredAuditEvent(
        string Id,
        string ActionType,
        string EntityType,
        string EntityId,
        string Summary,
        string NewValueJson);

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
