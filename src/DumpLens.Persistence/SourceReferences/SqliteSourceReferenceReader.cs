using System.Globalization;
using DumpLens.Application.SourceReferences;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpLens.Persistence.SourceReferences;

public sealed class SqliteSourceReferenceReader : ISourceReferenceReader
{
    private const string LoadFailedOperation = "source_reference_load_failed";
    private const string LoadedOperation = "source_reference_loaded";
    private const string MissingOperation = "source_reference_missing";
    private const string RequestedOperation = "source_reference_requested";

    private readonly ILogger<SqliteSourceReferenceReader> _logger;

    public SqliteSourceReferenceReader(ILogger<SqliteSourceReferenceReader>? logger = null)
    {
        _logger = logger ?? NullLogger<SqliteSourceReferenceReader>.Instance;
    }

    public async Task<SourceReferenceDetail?> LoadAsync(
        LoadSourceReferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = Normalize(request);
        var failureStage = "validation";

        _logger.LogInformation(
            "Source reference requested. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} source_artifact_id={SourceArtifactId} message_id={MessageId}",
            RequestedOperation,
            normalizedRequest.CorrelationId,
            normalizedRequest.CaseId,
            normalizedRequest.SourceImportId,
            normalizedRequest.SourceArtifactId ?? "-",
            normalizedRequest.MessageId ?? "-");

        try
        {
            await using var connection = new SqliteConnection(BuildConnectionString(normalizedRequest.CaseDatabasePath));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

            failureStage = "source_import";
            var sourceImport = await LoadSourceImportAsync(connection, normalizedRequest, cancellationToken).ConfigureAwait(false);
            if (sourceImport is null)
            {
                _logger.LogWarning(
                    "Source reference missing. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} source_artifact_id={SourceArtifactId} message_id={MessageId}",
                    MissingOperation,
                    normalizedRequest.CorrelationId,
                    normalizedRequest.CaseId,
                    normalizedRequest.SourceImportId,
                    normalizedRequest.SourceArtifactId ?? "-",
                    normalizedRequest.MessageId ?? "-");
                return null;
            }

            failureStage = "message_reference";
            var messageReference = await LoadMessageReferenceAsync(connection, normalizedRequest, cancellationToken).ConfigureAwait(false);
            var effectiveArtifactId = NormalizeOptional(normalizedRequest.SourceArtifactId)
                ?? NormalizeOptional(messageReference?.SourceArtifactId);

            failureStage = "artifact_reference";
            var artifactReference = await LoadArtifactReferenceAsync(
                connection,
                normalizedRequest with { SourceArtifactId = effectiveArtifactId },
                cancellationToken).ConfigureAwait(false);

            var detail = new SourceReferenceDetail
            {
                CaseId = sourceImport.CaseId,
                SourceImportId = sourceImport.SourceImportId,
                SourceName = sourceImport.SourceName,
                SourceType = sourceImport.SourceType,
                Platform = sourceImport.Platform,
                ImportStatus = sourceImport.ImportStatus,
                OriginalFilename = sourceImport.OriginalFilename,
                StoredRelativePath = sourceImport.StoredRelativePath,
                FileSizeBytes = sourceImport.FileSizeBytes,
                FileSha256 = sourceImport.FileSha256,
                ImportedAtUtc = sourceImport.ImportedAtUtc,
                HasSourceMetadata = sourceImport.HasSourceMetadata,
                WasArtifactReferenceRequested = effectiveArtifactId is not null,
                WasMessageReferenceRequested = normalizedRequest.MessageId is not null,
                ArtifactReference = artifactReference,
                MessageReference = messageReference
            };

            _logger.LogInformation(
                "Source reference loaded. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} source_artifact_id={SourceArtifactId} message_id={MessageId}",
                LoadedOperation,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                artifactReference?.SourceArtifactId ?? effectiveArtifactId ?? "-",
                messageReference?.MessageId ?? normalizedRequest.MessageId ?? "-");

            return detail;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Source reference load failed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} source_artifact_id={SourceArtifactId} message_id={MessageId} failure_stage={FailureStage} failure_type={FailureType}",
                LoadFailedOperation,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                normalizedRequest.SourceArtifactId ?? "-",
                normalizedRequest.MessageId ?? "-",
                failureStage,
                exception.GetType().Name);
            throw;
        }
    }

    private static async Task<SourceImportRow?> LoadSourceImportAsync(
        SqliteConnection connection,
        NormalizedRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                case_id,
                source_name,
                source_type,
                platform,
                import_status,
                original_filename,
                stored_file_path,
                file_size_bytes,
                file_sha256,
                imported_at_utc,
                source_metadata_json
            FROM source_imports
            WHERE case_id = $caseId
              AND id = $sourceImportId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$caseId", request.CaseId);
        command.Parameters.AddWithValue("$sourceImportId", request.SourceImportId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new SourceImportRow(
            SourceImportId: reader.GetString(0),
            CaseId: reader.GetString(1),
            SourceName: reader.GetString(2),
            SourceType: reader.GetString(3),
            Platform: reader.IsDBNull(4) ? null : reader.GetString(4),
            ImportStatus: reader.GetString(5),
            OriginalFilename: reader.GetString(6),
            StoredRelativePath: NormalizeStoredRelativePath(
                reader.IsDBNull(7) ? null : reader.GetString(7),
                request.CasePackageRootPath),
            FileSizeBytes: reader.IsDBNull(8) ? null : reader.GetInt64(8),
            FileSha256: reader.GetString(9),
            ImportedAtUtc: ParseUtc(reader.GetString(10)),
            HasSourceMetadata: !reader.IsDBNull(11) && !string.IsNullOrWhiteSpace(reader.GetString(11)));
    }

    private static async Task<MessageSourceReferenceDetail?> LoadMessageReferenceAsync(
        SqliteConnection connection,
        NormalizedRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MessageId is null)
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                source_artifact_id,
                provider_message_id,
                source_thread_id,
                event_time_utc,
                deleted_status,
                message_body_sha256,
                original_metadata_json
            FROM messages
            WHERE case_id = $caseId
              AND source_import_id = $sourceImportId
              AND id = $messageId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$caseId", request.CaseId);
        command.Parameters.AddWithValue("$sourceImportId", request.SourceImportId);
        command.Parameters.AddWithValue("$messageId", request.MessageId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new MessageSourceReferenceDetail
        {
            MessageId = reader.GetString(0),
            SourceArtifactId = reader.IsDBNull(1) ? null : reader.GetString(1),
            ProviderMessageId = reader.IsDBNull(2) ? null : reader.GetString(2),
            SourceThreadId = reader.IsDBNull(3) ? null : reader.GetString(3),
            EventTimeUtc = reader.IsDBNull(4) ? null : ParseUtc(reader.GetString(4)),
            DeletedStatus = reader.IsDBNull(5) ? null : reader.GetString(5),
            MessageHashPrefix = BuildHashPrefix(reader.IsDBNull(6) ? null : reader.GetString(6)),
            HasOriginalMetadata = !reader.IsDBNull(7) && !string.IsNullOrWhiteSpace(reader.GetString(7))
        };
    }

    private static async Task<SourceArtifactReferenceDetail?> LoadArtifactReferenceAsync(
        SqliteConnection connection,
        NormalizedRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SourceArtifactId is null)
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                artifact_type,
                artifact_locator,
                raw_metadata_json
            FROM source_artifacts
            WHERE case_id = $caseId
              AND source_import_id = $sourceImportId
              AND id = $sourceArtifactId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$caseId", request.CaseId);
        command.Parameters.AddWithValue("$sourceImportId", request.SourceImportId);
        command.Parameters.AddWithValue("$sourceArtifactId", request.SourceArtifactId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new SourceArtifactReferenceDetail
        {
            SourceArtifactId = reader.GetString(0),
            ArtifactType = reader.GetString(1),
            ArtifactLocator = reader.IsDBNull(2) ? null : reader.GetString(2),
            HasOriginalMetadata = !reader.IsDBNull(3) && !string.IsNullOrWhiteSpace(reader.GetString(3))
        };
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

    private static string BuildHashPrefix(string? hash)
    {
        var normalizedHash = NormalizeOptional(hash);
        if (normalizedHash is null)
        {
            return "Not recorded";
        }

        return normalizedHash.Length <= 12
            ? normalizedHash
            : normalizedHash[..12];
    }

    private static NormalizedRequest Normalize(LoadSourceReferenceRequest request)
    {
        return new NormalizedRequest(
            CaseId: NormalizeRequired(request.CaseId, nameof(request.CaseId)),
            CaseDatabasePath: NormalizeAbsoluteFilePath(request.CaseDatabasePath, nameof(request.CaseDatabasePath)),
            CasePackageRootPath: NormalizeAbsoluteDirectoryPath(request.CasePackageRootPath, nameof(request.CasePackageRootPath)),
            SourceImportId: NormalizeRequired(request.SourceImportId, nameof(request.SourceImportId)),
            SourceArtifactId: NormalizeOptional(request.SourceArtifactId),
            MessageId: NormalizeOptional(request.MessageId),
            CorrelationId: NormalizeOptional(request.CorrelationId) ?? Guid.NewGuid().ToString("N"));
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

    private static string? NormalizeStoredRelativePath(string? storedFilePath, string casePackageRootPath)
    {
        var normalizedPath = NormalizeOptional(storedFilePath);
        if (normalizedPath is null)
        {
            return null;
        }

        if (!Path.IsPathRooted(normalizedPath))
        {
            return normalizedPath.Replace('\\', '/');
        }

        var fullPath = Path.GetFullPath(normalizedPath);
        var packageRoot = Path.GetFullPath(casePackageRootPath);
        var packageRootWithSeparator = packageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (fullPath.StartsWith(packageRootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(packageRoot, fullPath).Replace('\\', '/');
        }

        return Path.GetFileName(fullPath);
    }

    private sealed record NormalizedRequest(
        string CaseId,
        string CaseDatabasePath,
        string CasePackageRootPath,
        string SourceImportId,
        string? SourceArtifactId,
        string? MessageId,
        string CorrelationId);

    private sealed record SourceImportRow(
        string SourceImportId,
        string CaseId,
        string SourceName,
        string SourceType,
        string? Platform,
        string ImportStatus,
        string OriginalFilename,
        string? StoredRelativePath,
        long? FileSizeBytes,
        string FileSha256,
        DateTimeOffset ImportedAtUtc,
        bool HasSourceMetadata);
}
