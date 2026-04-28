using System.Globalization;
using System.Text.Json;
using DumpLens.Application.CasePackages;
using DumpLens.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpLens.Persistence.CasePackages;

public sealed class CasePackageService : ICasePackageService
{
    private const string AppName = "DumpLens";
    private const string DatabaseRelativePath = "case.dlensdb";
    private const string ManifestFileName = "manifest.json";
    private const string OperationName = "case_package_create";
    private const string PackageVersion = "1";

    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        WriteIndented = true
    };

    private static readonly FolderDefinition[] StandardFolders =
    {
        new("imports", "imports", ["imports"]),
        new("indexes", "indexes", ["indexes"]),
        new("attachments", "attachments", ["attachments"]),
        new("attachments_thumbnails", "attachments/thumbnails", ["attachments", "thumbnails"]),
        new("attachments_extracted_text", "attachments/extracted_text", ["attachments", "extracted_text"]),
        new("attachments_media_cache", "attachments/media_cache", ["attachments", "media_cache"]),
        new("reports", "reports", ["reports"]),
        new("exports", "exports", ["exports"]),
        new("logs", "logs", ["logs"]),
        new("backups", "backups", ["backups"])
    };

    private readonly ILogger<CasePackageService> _logger;

    public CasePackageService(ILogger<CasePackageService>? logger = null)
    {
        _logger = logger ?? NullLogger<CasePackageService>.Instance;
    }

    public async Task<CasePackageCreateResult> CreateAsync(
        CasePackageCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RootDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CaseId);

        if (request.PreparationMode != CasePackagePreparationMode.Copy)
        {
            throw new NotSupportedException("Only copy-mode case package preparation is supported in T0008.");
        }

        var rootDirectoryPath = Path.GetFullPath(request.RootDirectoryPath);
        if (!Path.IsPathRooted(rootDirectoryPath))
        {
            throw new ArgumentException("The root directory path must be absolute.", nameof(request.RootDirectoryPath));
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var packageId = Guid.NewGuid().ToString("N");
        var packageFolderName = ResolvePackageFolderName(request);
        var packageRootPath = SafePathName.ResolvePathWithinRoot(rootDirectoryPath, packageFolderName);
        var manifestPath = Path.Combine(packageRootPath, ManifestFileName);
        var databasePath = Path.Combine(packageRootPath, DatabaseRelativePath);
        var createdAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        _logger.LogInformation(
            "Case package creation started. operation={Operation} correlation_id={CorrelationId} package_id={PackageId} case_id={CaseId} preparation_mode={PreparationMode}",
            OperationName,
            correlationId,
            packageId,
            request.CaseId,
            request.PreparationMode.ToString().ToLowerInvariant());

        try
        {
            Directory.CreateDirectory(rootDirectoryPath);

            if (Directory.Exists(packageRootPath) || File.Exists(packageRootPath))
            {
                throw new InvalidOperationException("The target case package path already exists.");
            }

            Directory.CreateDirectory(packageRootPath);
            LogDirectoryCreated(correlationId, packageId, request.CaseId, "package_root", ".");

            var folders = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var folder in StandardFolders)
            {
                var folderPath = SafePathName.ResolvePathWithinRoot(packageRootPath, folder.Segments);
                Directory.CreateDirectory(folderPath);
                folders[folder.Key] = folder.RelativePath;
                LogDirectoryCreated(correlationId, packageId, request.CaseId, folder.Key, folder.RelativePath);
            }

            var manifest = new CasePackageManifest
            {
                PackageVersion = PackageVersion,
                PackageId = packageId,
                CaseId = request.CaseId,
                CaseNumber = NormalizeOptionalValue(request.CaseNumber),
                Title = NormalizeOptionalValue(request.Title),
                CreatedAtUtc = createdAtUtc,
                AppName = AppName,
                DatabaseRelativePath = DatabaseRelativePath,
                PreparationMode = request.PreparationMode.ToString().ToLowerInvariant(),
                Folders = folders
            };

            var manifestJson = JsonSerializer.Serialize(manifest, ManifestSerializerOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Manifest written. operation={Operation} correlation_id={CorrelationId} package_id={PackageId} case_id={CaseId} manifest_file={ManifestFile} database_relative_path={DatabaseRelativePath}",
                OperationName,
                correlationId,
                packageId,
                request.CaseId,
                ManifestFileName,
                DatabaseRelativePath);

            _logger.LogInformation(
                "Case package creation completed. operation={Operation} correlation_id={CorrelationId} package_id={PackageId} case_id={CaseId} directory_count={DirectoryCount} database_relative_path={DatabaseRelativePath}",
                OperationName,
                correlationId,
                packageId,
                request.CaseId,
                folders.Count + 1,
                DatabaseRelativePath);

            return new CasePackageCreateResult
            {
                PackageId = packageId,
                CaseId = request.CaseId,
                PackageRootPath = packageRootPath,
                ManifestPath = manifestPath,
                DatabasePath = databasePath,
                DatabaseRelativePath = DatabaseRelativePath,
                Folders = folders,
                Manifest = manifest
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Case package creation failed. operation={Operation} correlation_id={CorrelationId} package_id={PackageId} case_id={CaseId} package_root_created={PackageRootCreated}",
                OperationName,
                correlationId,
                packageId,
                request.CaseId,
                Directory.Exists(packageRootPath));
            throw;
        }
    }

    private void LogDirectoryCreated(
        string correlationId,
        string packageId,
        string caseId,
        string directoryKey,
        string relativePath)
    {
        _logger.LogInformation(
            "Case package directory created. operation={Operation} correlation_id={CorrelationId} package_id={PackageId} case_id={CaseId} directory_key={DirectoryKey} relative_path={RelativePath}",
            OperationName,
            correlationId,
            packageId,
            caseId,
            directoryKey,
            relativePath);
    }

    private static string ResolvePackageFolderName(CasePackageCreateRequest request)
    {
        var requestedFolderName = NormalizeOptionalValue(request.RequestedFolderName);
        if (requestedFolderName is not null)
        {
            return SafePathName.Create(requestedFolderName, nameof(request.RequestedFolderName)).Value;
        }

        var nameParts = new[]
        {
            NormalizeOptionalValue(request.CaseNumber),
            NormalizeOptionalValue(request.Title)
        }.Where(static value => value is not null);

        var fallbackName = string.Join(" - ", nameParts!);
        if (string.IsNullOrWhiteSpace(fallbackName))
        {
            fallbackName = request.CaseId;
        }

        return SafePathName.Create(fallbackName, nameof(request.RequestedFolderName)).Value;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record FolderDefinition(string Key, string RelativePath, string[] Segments);
}
