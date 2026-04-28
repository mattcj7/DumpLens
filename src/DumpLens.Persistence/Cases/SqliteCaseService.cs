using System.Globalization;
using System.Text.Json;
using DumpLens.Application.Audit;
using DumpLens.Application.CasePackages;
using DumpLens.Application.Cases;
using DumpLens.Persistence.Audit;
using DumpLens.Persistence.CasePackages;
using DumpLens.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpLens.Persistence.Cases;

public sealed class SqliteCaseService : ICaseService
{
    private const string OperationName = "case_creation";
    private const string CaseStatusOpen = "open";
    private const string CaseCreatedActionType = "case_created";
    private const string CaseEntityType = "case";

    private readonly Func<string, IAuditLogger> _auditLoggerFactory;
    private readonly ICasePackageService _casePackageService;
    private readonly ICaseRepository _caseRepository;
    private readonly ILogger<SqliteCaseService> _logger;
    private readonly SqliteMigrationRunner _migrationRunner;

    public SqliteCaseService(ILogger<SqliteCaseService>? logger = null)
        : this(
            new CasePackageService(),
            new SqliteMigrationRunner(),
            new SqliteCaseRepository(),
            null,
            logger)
    {
    }

    public SqliteCaseService(
        ICasePackageService casePackageService,
        SqliteMigrationRunner migrationRunner,
        ICaseRepository caseRepository,
        Func<string, IAuditLogger>? auditLoggerFactory = null,
        ILogger<SqliteCaseService>? logger = null)
    {
        _casePackageService = casePackageService ?? throw new ArgumentNullException(nameof(casePackageService));
        _migrationRunner = migrationRunner ?? throw new ArgumentNullException(nameof(migrationRunner));
        _caseRepository = caseRepository ?? throw new ArgumentNullException(nameof(caseRepository));
        _auditLoggerFactory = auditLoggerFactory ?? (connectionString => new SqliteAuditLogger(connectionString));
        _logger = logger ?? NullLogger<SqliteCaseService>.Instance;
    }

    public async Task<CreateCaseResult> CreateAsync(
        CreateCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = NormalizeCorrelationId(request.CorrelationId);
        var caseId = Guid.NewGuid().ToString("N");
        var failureStage = "validation";
        CasePackageCreateResult? packageResult = null;
        string? auditEventId = null;

        _logger.LogInformation(
            "Case creation started. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} case_number_present={CaseNumberPresent} incident_start_present={IncidentStartPresent} incident_end_present={IncidentEndPresent}",
            OperationName,
            correlationId,
            caseId,
            !string.IsNullOrWhiteSpace(request.CaseNumber),
            request.IncidentStartUtc.HasValue,
            request.IncidentEndUtc.HasValue);

        try
        {
            var normalizedRequest = ValidateAndNormalize(request);

            failureStage = "case_package_create";
            packageResult = await _casePackageService.CreateAsync(
                new CasePackageCreateRequest
                {
                    RootDirectoryPath = normalizedRequest.ParentDirectoryPath,
                    CaseId = caseId,
                    CaseNumber = normalizedRequest.CaseNumber,
                    Title = normalizedRequest.Title,
                    RequestedFolderName = normalizedRequest.RequestedPackageFolderName
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Case package created. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} package_id={PackageId} database_relative_path={DatabaseRelativePath}",
                OperationName,
                correlationId,
                caseId,
                packageResult.PackageId,
                packageResult.DatabaseRelativePath);

            var connectionString = BuildConnectionString(packageResult.DatabasePath);

            failureStage = "database_migration";
            await _migrationRunner.RunMigrationsAsync(connectionString, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Case database migrated. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} package_id={PackageId} database_file_name={DatabaseFileName}",
                OperationName,
                correlationId,
                caseId,
                packageResult.PackageId,
                Path.GetFileName(packageResult.DatabasePath));

            var createdAtUtc = DateTimeOffset.UtcNow;
            var caseRecord = new CaseRecord
            {
                Id = caseId,
                CaseNumber = normalizedRequest.CaseNumber,
                Title = normalizedRequest.Title,
                IncidentType = normalizedRequest.IncidentType,
                IncidentStartUtc = normalizedRequest.IncidentStartUtc,
                IncidentEndUtc = normalizedRequest.IncidentEndUtc,
                IncidentTimezone = normalizedRequest.IncidentTimezone,
                IncidentLocationText = normalizedRequest.IncidentLocationText,
                LeadInvestigator = normalizedRequest.LeadInvestigator,
                Agency = normalizedRequest.Agency,
                Summary = normalizedRequest.Summary,
                CaseStatus = CaseStatusOpen,
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = createdAtUtc
            };

            failureStage = "case_record_insert";
            var caseSummary = await _caseRepository.InsertAsync(connectionString, caseRecord, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Case record inserted. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} package_id={PackageId} case_status={CaseStatus}",
                OperationName,
                correlationId,
                caseId,
                packageResult.PackageId,
                caseSummary.CaseStatus);

            failureStage = "audit_event_write";
            var auditLogger = _auditLoggerFactory(connectionString);
            var auditWrite = await auditLogger.WriteAsync(
                new AuditEventDraft
                {
                    CaseId = caseId,
                    UserId = normalizedRequest.CreatedByUserId,
                    ActionType = CaseCreatedActionType,
                    EntityType = CaseEntityType,
                    EntityId = caseId,
                    Summary = "Case created.",
                    NewValueJson = CreateAuditNewValueJson(caseId, packageResult.PackageId, normalizedRequest.CaseNumber),
                    EventTimeUtc = DateTimeOffset.UtcNow,
                    CorrelationId = correlationId
                },
                cancellationToken).ConfigureAwait(false);
            auditEventId = auditWrite.AuditEvent.Id;

            _logger.LogInformation(
                "Case creation audit event written. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} package_id={PackageId} audit_event_id={AuditEventId} action_type={ActionType}",
                OperationName,
                correlationId,
                caseId,
                packageResult.PackageId,
                auditEventId,
                CaseCreatedActionType);

            failureStage = "completed";
            _logger.LogInformation(
                "Case creation completed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} package_id={PackageId} audit_event_id={AuditEventId}",
                OperationName,
                correlationId,
                caseId,
                packageResult.PackageId,
                auditEventId);

            return new CreateCaseResult
            {
                CaseId = caseId,
                PackageId = packageResult.PackageId,
                CaseNumber = caseSummary.CaseNumber,
                Title = caseSummary.Title,
                PackageRootPath = packageResult.PackageRootPath,
                DatabasePath = packageResult.DatabasePath,
                ManifestPath = packageResult.ManifestPath,
                CreatedAtUtc = caseSummary.CreatedAtUtc,
                AuditEventId = auditEventId,
                CorrelationId = correlationId
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Case creation failed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} package_id={PackageId} failure_stage={FailureStage} failure_type={FailureType} audit_event_id={AuditEventId}",
                OperationName,
                correlationId,
                caseId,
                packageResult?.PackageId,
                failureStage,
                exception.GetType().Name,
                auditEventId);
            throw;
        }
    }

    private static NormalizedCreateCaseRequest ValidateAndNormalize(CreateCaseRequest request)
    {
        var title = NormalizeRequired(request.Title, nameof(request.Title));
        var parentDirectoryPath = NormalizeRequired(request.ParentDirectoryPath, nameof(request.ParentDirectoryPath));

        if (ContainsTraversalSegment(parentDirectoryPath))
        {
            throw new ArgumentException(
                "The parent directory path must not contain traversal segments.",
                nameof(request.ParentDirectoryPath));
        }

        var fullParentDirectoryPath = Path.GetFullPath(parentDirectoryPath);
        if (!Path.IsPathRooted(fullParentDirectoryPath))
        {
            throw new ArgumentException(
                "The parent directory path must be absolute.",
                nameof(request.ParentDirectoryPath));
        }

        if (!Directory.Exists(fullParentDirectoryPath))
        {
            throw new DirectoryNotFoundException("The parent directory path must exist and be a directory.");
        }

        var requestedPackageFolderName = NormalizeOptional(request.RequestedPackageFolderName);
        if (requestedPackageFolderName is not null &&
            (ContainsTraversalSegment(requestedPackageFolderName) ||
             requestedPackageFolderName.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
             requestedPackageFolderName.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "The requested package folder name must be a safe folder name, not a path.",
                nameof(request.RequestedPackageFolderName));
        }

        var incidentStartUtc = request.IncidentStartUtc?.ToUniversalTime();
        var incidentEndUtc = request.IncidentEndUtc?.ToUniversalTime();

        if (incidentStartUtc.HasValue &&
            incidentEndUtc.HasValue &&
            incidentEndUtc.Value < incidentStartUtc.Value)
        {
            throw new ArgumentException(
                "Incident end UTC cannot be earlier than incident start UTC.",
                nameof(request.IncidentEndUtc));
        }

        return new NormalizedCreateCaseRequest(
            NormalizeOptional(request.CaseNumber),
            title,
            NormalizeOptional(request.IncidentType),
            incidentStartUtc,
            incidentEndUtc,
            NormalizeOptional(request.IncidentTimezone),
            NormalizeOptional(request.IncidentLocationText),
            NormalizeOptional(request.LeadInvestigator),
            NormalizeOptional(request.Agency),
            NormalizeOptional(request.Summary),
            fullParentDirectoryPath,
            requestedPackageFolderName,
            NormalizeOptional(request.CreatedByUserId),
            NormalizeOptional(request.CreatedByDisplayName));
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

    private static string CreateAuditNewValueJson(string caseId, string packageId, string? caseNumber)
    {
        var auditValue = new
        {
            case_id = caseId,
            package_id = packageId,
            case_number_present = !string.IsNullOrWhiteSpace(caseNumber),
            case_status = CaseStatusOpen,
            database_relative_path = "case.dlensdb",
            created_at_utc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        return JsonSerializer.Serialize(auditValue);
    }

    private static string NormalizeCorrelationId(string? correlationId)
    {
        return NormalizeOptional(correlationId) ?? Guid.NewGuid().ToString("N");
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static bool ContainsTraversalSegment(string value)
    {
        var segments = value.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));
    }

    private sealed record NormalizedCreateCaseRequest(
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
        string ParentDirectoryPath,
        string? RequestedPackageFolderName,
        string? CreatedByUserId,
        string? CreatedByDisplayName);
}
