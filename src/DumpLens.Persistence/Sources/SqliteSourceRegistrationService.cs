using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DumpLens.Application.Audit;
using DumpLens.Application.FileHashing;
using DumpLens.Application.Sources;
using DumpLens.Core.Storage;
using DumpLens.Persistence.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpLens.Persistence.Sources;

public sealed class SqliteSourceRegistrationService : ISourceRegistrationService
{
    private const string AppName = "DumpLens";
    private const string AuditActionType = "source_registered";
    private const string AuditEntityType = "source_import";
    private const string CopyMode = "copy";
    private const string ImportStatusRegistered = "registered";
    private const string ManifestFileName = "manifest.json";
    private const string ManifestVersion = "1";
    private const string OperationName = "source_registration";
    private const string Sha256FileName = "sha256.txt";
    private const int SourceFolderIdLength = 12;

    private static readonly JsonSerializerOptions AuditSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly Func<string, IAuditLogger> _auditLoggerFactory;
    private readonly IFileHashService _fileHashService;
    private readonly ILogger<SqliteSourceRegistrationService> _logger;
    private readonly ISourceImportRepository _sourceImportRepository;

    public SqliteSourceRegistrationService(
        IFileHashService fileHashService,
        ISourceImportRepository sourceImportRepository,
        Func<string, IAuditLogger>? auditLoggerFactory = null,
        ILogger<SqliteSourceRegistrationService>? logger = null)
    {
        _fileHashService = fileHashService ?? throw new ArgumentNullException(nameof(fileHashService));
        _sourceImportRepository = sourceImportRepository ?? throw new ArgumentNullException(nameof(sourceImportRepository));
        _auditLoggerFactory = auditLoggerFactory ?? (connectionString => new SqliteAuditLogger(connectionString));
        _logger = logger ?? NullLogger<SqliteSourceRegistrationService>.Instance;
    }

    public async Task<RegisterSourceResult> RegisterAsync(
        RegisterSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = NormalizeCorrelationId(request.CorrelationId);
        var sourceImportId = Guid.NewGuid().ToString("N");
        var auditEventId = default(string);
        var failureStage = "validation";
        var safeCaseId = NormalizeOptional(request.CaseId);
        var safePlatform = NormalizeOptional(request.Platform);
        var selectedFileExtension = TryGetFileExtension(request.SelectedSourceFilePath);
        var sourceFolderRelativePath = default(string);

        _logger.LogInformation(
            "Source registration started. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} source_type={SourceType} platform={Platform} source_file_extension={SourceFileExtension} original_filename_override_present={OriginalFilenameOverridePresent} source_metadata_present={SourceMetadataPresent}",
            OperationName,
            correlationId,
            safeCaseId,
            sourceImportId,
            NormalizeOptional(request.SourceType),
            safePlatform,
            selectedFileExtension,
            !string.IsNullOrWhiteSpace(request.OriginalFilenameOverride),
            !string.IsNullOrWhiteSpace(request.SourceMetadataJson));

        try
        {
            var normalizedRequest = ValidateAndNormalize(request, correlationId);
            var connectionString = BuildConnectionString(normalizedRequest.CaseDatabasePath);

            failureStage = "case_validation";
            var caseExists = await _sourceImportRepository.CaseExistsAsync(connectionString, normalizedRequest.CaseId, cancellationToken)
                .ConfigureAwait(false);
            if (!caseExists)
            {
                throw new InvalidOperationException("The requested case_id was not found in the case database.");
            }

            var importedAtUtc = DateTimeOffset.UtcNow;
            var importsRootPath = SafePathName.ResolvePathWithinRoot(normalizedRequest.CasePackageRootPath, "imports");
            Directory.CreateDirectory(importsRootPath);

            failureStage = "source_folder_create";
            var sourceFolderPath = CreateUniqueSourceFolder(importsRootPath, sourceImportId);
            var originalFolderPath = SafePathName.ResolvePathWithinRoot(sourceFolderPath, "original");
            Directory.CreateDirectory(originalFolderPath);
            sourceFolderRelativePath = ToRelativePath(normalizedRequest.CasePackageRootPath, sourceFolderPath);

            _logger.LogInformation(
                "Source folder created. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} source_folder_relative_path={SourceFolderRelativePath}",
                OperationName,
                correlationId,
                normalizedRequest.CaseId,
                sourceImportId,
                sourceFolderRelativePath);

            failureStage = "source_file_copy";
            var storedFilePath = SafePathName.ResolvePathWithinRoot(originalFolderPath, normalizedRequest.StoredFileName);
            await CopyFileAsync(normalizedRequest.SelectedSourceFilePath, storedFilePath, cancellationToken).ConfigureAwait(false);
            var storedFileInfo = new FileInfo(storedFilePath);

            _logger.LogInformation(
                "Source file copied. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} source_folder_relative_path={SourceFolderRelativePath} stored_file_extension={StoredFileExtension} file_size_bytes={FileSizeBytes}",
                OperationName,
                correlationId,
                normalizedRequest.CaseId,
                sourceImportId,
                sourceFolderRelativePath,
                Path.GetExtension(storedFilePath),
                storedFileInfo.Length);

            failureStage = "source_hash_compute";
            var originalHash = await _fileHashService.ComputeHashAsync(
                new FileHashRequest
                {
                    FilePath = normalizedRequest.SelectedSourceFilePath,
                    CorrelationId = correlationId
                },
                cancellationToken).ConfigureAwait(false);
            var copiedHash = await _fileHashService.ComputeHashAsync(
                new FileHashRequest
                {
                    FilePath = storedFilePath,
                    CorrelationId = correlationId
                },
                cancellationToken).ConfigureAwait(false);

            if (!string.Equals(originalHash.HexDigest, copiedHash.HexDigest, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The copied source file hash does not match the original source file hash.");
            }

            _logger.LogInformation(
                "Source file hashed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} file_size_bytes={FileSizeBytes} hash_prefix={HashPrefix} copied_hash_verified={CopiedHashVerified}",
                OperationName,
                correlationId,
                normalizedRequest.CaseId,
                sourceImportId,
                copiedHash.FileSizeBytes,
                GetHashPrefix(copiedHash.HexDigest),
                true);

            failureStage = "sha256_write";
            var sha256FilePath = await _fileHashService.WriteSha256FileAsync(
                copiedHash,
                sourceFolderPath,
                Sha256FileName,
                cancellationToken).ConfigureAwait(false);

            failureStage = "manifest_write";
            var storedRelativePath = ToRelativePath(normalizedRequest.CasePackageRootPath, storedFilePath);
            var sha256RelativePath = ToRelativePath(normalizedRequest.CasePackageRootPath, sha256FilePath);
            var manifestPath = SafePathName.ResolvePathWithinRoot(sourceFolderPath, ManifestFileName);
            var manifest = new SourceImportManifest
            {
                ManifestVersion = ManifestVersion,
                SourceImportId = sourceImportId,
                CaseId = normalizedRequest.CaseId,
                SourceName = normalizedRequest.SourceName,
                SourceType = normalizedRequest.SourceType,
                Platform = normalizedRequest.Platform,
                OriginalFilename = normalizedRequest.OriginalFilename,
                StoredRelativePath = storedRelativePath,
                FileSizeBytes = copiedHash.FileSizeBytes,
                FileSha256 = copiedHash.HexDigest,
                ImportedAtUtc = FormatUtc(importedAtUtc),
                SourceFolderRelativePath = sourceFolderRelativePath,
                Sha256RelativePath = sha256RelativePath,
                AppName = AppName,
                CopyMode = CopyMode
            };

            var manifestJson = JsonSerializer.Serialize(manifest, ManifestSerializerOptions);
            await File.WriteAllTextAsync(
                    manifestPath,
                    manifestJson,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Source manifest written. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} manifest_relative_path={ManifestRelativePath}",
                OperationName,
                correlationId,
                normalizedRequest.CaseId,
                sourceImportId,
                ToRelativePath(normalizedRequest.CasePackageRootPath, manifestPath));

            failureStage = "source_import_insert";
            var record = new SourceImportRecord
            {
                Id = sourceImportId,
                CaseId = normalizedRequest.CaseId,
                SourceName = normalizedRequest.SourceName,
                SourceType = normalizedRequest.SourceType,
                Platform = normalizedRequest.Platform,
                OwnerPersonId = normalizedRequest.OwnerPersonId,
                DeviceId = normalizedRequest.DeviceId,
                PlatformAccountId = normalizedRequest.PlatformAccountId,
                ExtractionType = normalizedRequest.ExtractionType,
                ProviderReturnType = normalizedRequest.ProviderReturnType,
                OriginalFilename = normalizedRequest.OriginalFilename,
                OriginalFilePath = normalizedRequest.SelectedSourceFilePath,
                StoredFilePath = storedRelativePath,
                FileSizeBytes = copiedHash.FileSizeBytes,
                FileSha256 = copiedHash.HexDigest,
                FileMd5 = null,
                ImportedByUserId = normalizedRequest.ImportedByUserId,
                ImportedAtUtc = importedAtUtc,
                ImportStatus = ImportStatusRegistered,
                RecordCount = 0,
                WarningCount = 0,
                Notes = normalizedRequest.Notes,
                SourceMetadataJson = normalizedRequest.SourceMetadataJson,
                CreatedAtUtc = importedAtUtc,
                UpdatedAtUtc = importedAtUtc
            };

            await _sourceImportRepository.InsertAsync(connectionString, record, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Source imports row inserted. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} import_status={ImportStatus} record_count={RecordCount} warning_count={WarningCount}",
                OperationName,
                correlationId,
                normalizedRequest.CaseId,
                sourceImportId,
                ImportStatusRegistered,
                0,
                0);

            failureStage = "audit_event_write";
            var auditWrite = await _auditLoggerFactory(connectionString).WriteAsync(
                new AuditEventDraft
                {
                    CaseId = normalizedRequest.CaseId,
                    UserId = normalizedRequest.ImportedByUserId,
                    ActionType = AuditActionType,
                    EntityType = AuditEntityType,
                    EntityId = sourceImportId,
                    Summary = "Source registered.",
                    NewValueJson = CreateAuditNewValueJson(
                        normalizedRequest.CaseId,
                        sourceImportId,
                        normalizedRequest.SourceType,
                        normalizedRequest.Platform,
                        storedRelativePath,
                        copiedHash.FileSizeBytes,
                        copiedHash.HexDigest,
                        importedAtUtc),
                    EventTimeUtc = importedAtUtc,
                    CorrelationId = correlationId
                },
                cancellationToken).ConfigureAwait(false);
            auditEventId = auditWrite.AuditEvent.Id;

            _logger.LogInformation(
                "Source registration audit event written. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} audit_event_id={AuditEventId} action_type={ActionType}",
                OperationName,
                correlationId,
                normalizedRequest.CaseId,
                sourceImportId,
                auditEventId,
                AuditActionType);

            failureStage = "completed";
            _logger.LogInformation(
                "Source registration completed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} source_folder_relative_path={SourceFolderRelativePath} audit_event_id={AuditEventId}",
                OperationName,
                correlationId,
                normalizedRequest.CaseId,
                sourceImportId,
                sourceFolderRelativePath,
                auditEventId);

            return new RegisterSourceResult
            {
                SourceImportId = sourceImportId,
                CaseId = normalizedRequest.CaseId,
                SourceName = normalizedRequest.SourceName,
                SourceType = normalizedRequest.SourceType,
                Platform = normalizedRequest.Platform,
                OriginalFilename = normalizedRequest.OriginalFilename,
                StoredFilePath = storedFilePath,
                SourceFolderPath = sourceFolderPath,
                ManifestPath = manifestPath,
                Sha256FilePath = sha256FilePath,
                FileSizeBytes = copiedHash.FileSizeBytes,
                FileSha256 = copiedHash.HexDigest,
                ImportedAtUtc = importedAtUtc,
                AuditEventId = auditEventId,
                CorrelationId = correlationId
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Source registration failed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} failure_stage={FailureStage} failure_type={FailureType} source_folder_relative_path={SourceFolderRelativePath} audit_event_id={AuditEventId}",
                OperationName,
                correlationId,
                safeCaseId,
                sourceImportId,
                failureStage,
                exception.GetType().Name,
                sourceFolderRelativePath,
                auditEventId);
            throw;
        }
    }

    private static NormalizedRegisterSourceRequest ValidateAndNormalize(RegisterSourceRequest request, string correlationId)
    {
        var caseId = NormalizeRequired(request.CaseId, nameof(request.CaseId));
        var caseDatabasePath = NormalizeRequired(request.CaseDatabasePath, nameof(request.CaseDatabasePath));
        var casePackageRootPath = NormalizeRequired(request.CasePackageRootPath, nameof(request.CasePackageRootPath));
        var selectedSourceFilePath = NormalizeRequired(request.SelectedSourceFilePath, nameof(request.SelectedSourceFilePath));
        var sourceType = NormalizeRequired(request.SourceType, nameof(request.SourceType));

        var fullCaseDatabasePath = NormalizeAbsoluteFilePath(caseDatabasePath, nameof(request.CaseDatabasePath));
        if (!File.Exists(fullCaseDatabasePath) || Directory.Exists(fullCaseDatabasePath))
        {
            throw new FileNotFoundException("The case database path must exist and point to a file.");
        }

        var fullCasePackageRootPath = NormalizeAbsoluteDirectoryPath(casePackageRootPath, nameof(request.CasePackageRootPath));
        if (!Directory.Exists(fullCasePackageRootPath))
        {
            throw new DirectoryNotFoundException("The case package root path must exist and point to a directory.");
        }

        EnsurePathWithinRoot(fullCaseDatabasePath, fullCasePackageRootPath, nameof(request.CaseDatabasePath));

        var fullSelectedSourceFilePath = NormalizeAbsoluteFilePath(selectedSourceFilePath, nameof(request.SelectedSourceFilePath));
        if (!File.Exists(fullSelectedSourceFilePath) || Directory.Exists(fullSelectedSourceFilePath))
        {
            throw new FileNotFoundException("The selected source file path must exist and point to a file.");
        }

        EnsureReadableFile(fullSelectedSourceFilePath, nameof(request.SelectedSourceFilePath));

        var originalFilename = ResolveOriginalFileName(request.OriginalFilenameOverride, fullSelectedSourceFilePath);
        var sourceName = NormalizeOptional(request.SourceName);
        if (sourceName is null)
        {
            sourceName = DeriveSourceName(originalFilename, correlationId);
        }

        return new NormalizedRegisterSourceRequest(
            caseId,
            fullCaseDatabasePath,
            fullCasePackageRootPath,
            fullSelectedSourceFilePath,
            sourceName,
            sourceType,
            NormalizeOptional(request.Platform),
            originalFilename,
            originalFilename,
            NormalizeOptional(request.ImportedByUserId),
            NormalizeOptional(request.Notes),
            NormalizeOptionalJson(request.SourceMetadataJson, nameof(request.SourceMetadataJson)),
            correlationId,
            NormalizeOptional(request.OwnerPersonId),
            NormalizeOptional(request.DeviceId),
            NormalizeOptional(request.PlatformAccountId),
            NormalizeOptional(request.ExtractionType),
            NormalizeOptional(request.ProviderReturnType));
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

    private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var sourceStream = new FileStream(
            sourcePath,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

        await using var destinationStream = new FileStream(
            destinationPath,
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.CreateNew,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

        await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
        await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string CreateAuditNewValueJson(
        string caseId,
        string sourceImportId,
        string sourceType,
        string? platform,
        string storedRelativePath,
        long fileSizeBytes,
        string fileSha256,
        DateTimeOffset importedAtUtc)
    {
        var auditValue = new
        {
            case_id = caseId,
            source_import_id = sourceImportId,
            source_type = sourceType,
            platform,
            stored_file_path = storedRelativePath,
            file_size_bytes = fileSizeBytes,
            file_sha256 = fileSha256,
            import_status = ImportStatusRegistered,
            imported_at_utc = FormatUtc(importedAtUtc)
        };

        return JsonSerializer.Serialize(auditValue, AuditSerializerOptions);
    }

    private static string CreateUniqueSourceFolder(string importsRootPath, string sourceImportId)
    {
        var baseFolderName = $"source_{sourceImportId[..Math.Min(SourceFolderIdLength, sourceImportId.Length)]}";
        var candidateFolderName = baseFolderName;
        var suffix = 2;

        while (true)
        {
            var candidatePath = SafePathName.ResolvePathWithinRoot(importsRootPath, candidateFolderName);
            if (!Directory.Exists(candidatePath) && !File.Exists(candidatePath))
            {
                Directory.CreateDirectory(candidatePath);
                return candidatePath;
            }

            candidateFolderName = $"{baseFolderName}_{suffix.ToString(CultureInfo.InvariantCulture)}";
            suffix++;
        }
    }

    private static string DeriveSourceName(string originalFilename, string correlationId)
    {
        var derivedName = NormalizeOptional(Path.GetFileNameWithoutExtension(originalFilename));
        return derivedName ?? $"source-{correlationId[..8]}";
    }

    private static void EnsurePathWithinRoot(string fullPath, string rootPath, string parameterName)
    {
        var fullRootPath = Path.GetFullPath(rootPath);
        var comparisonRoot = EnsureTrailingDirectorySeparator(fullRootPath);

        if (!fullPath.StartsWith(comparisonRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The path must remain within the case package root path.", parameterName);
        }
    }

    private static void EnsureReadableFile(string filePath, string parameterName)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Share = FileShare.Read
                });

            _ = stream.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException("The selected source file must be readable.", parameterName, exception);
        }
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string GetHashPrefix(string hash)
    {
        return hash.Length <= 12
            ? hash
            : hash[..12];
    }

    private static string NormalizeAbsoluteDirectoryPath(string path, string parameterName)
    {
        if (!Path.IsPathRooted(path))
        {
            throw new ArgumentException("The path must be absolute.", parameterName);
        }

        return Path.GetFullPath(path.Trim());
    }

    private static string NormalizeAbsoluteFilePath(string path, string parameterName)
    {
        if (!Path.IsPathRooted(path))
        {
            throw new ArgumentException("The path must be absolute.", parameterName);
        }

        return Path.GetFullPath(path.Trim());
    }

    private static string NormalizeCorrelationId(string? correlationId)
    {
        return NormalizeOptional(correlationId) ?? Guid.NewGuid().ToString("N");
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string? NormalizeOptionalJson(string? value, string parameterName)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        try
        {
            using var jsonDocument = JsonDocument.Parse(normalized);
            return JsonSerializer.Serialize(jsonDocument.RootElement, AuditSerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The provided JSON value must be valid JSON.", parameterName, exception);
        }
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

    private static string ResolveOriginalFileName(string? originalFilenameOverride, string selectedSourceFilePath)
    {
        var candidate = NormalizeOptional(originalFilenameOverride) ?? Path.GetFileName(selectedSourceFilePath);
        return SafePathName.Create(candidate, nameof(originalFilenameOverride)).Value;
    }

    private static string ToRelativePath(string rootPath, string fullPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, fullPath);
        return relativePath.Replace('\\', '/');
    }

    private static string? TryGetFileExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetExtension(path.Trim());
        }
        catch
        {
            return null;
        }
    }

    private sealed record NormalizedRegisterSourceRequest(
        string CaseId,
        string CaseDatabasePath,
        string CasePackageRootPath,
        string SelectedSourceFilePath,
        string SourceName,
        string SourceType,
        string? Platform,
        string OriginalFilename,
        string StoredFileName,
        string? ImportedByUserId,
        string? Notes,
        string? SourceMetadataJson,
        string CorrelationId,
        string? OwnerPersonId,
        string? DeviceId,
        string? PlatformAccountId,
        string? ExtractionType,
        string? ProviderReturnType);
}
