using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using DumpLens.Application.FileHashing;
using DumpLens.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpLens.Security.FileHashing;

public class Sha256FileHashService : IFileHashService
{
    private const int DefaultBufferSizeBytes = 81920;
    private const string DefaultOutputFileName = "sha256.txt";
    private const string HashOperationName = "file_hash";
    private const string SidecarWriteOperationName = "file_hash_sidecar_write";

    private readonly ILogger<Sha256FileHashService> _logger;

    public Sha256FileHashService(ILogger<Sha256FileHashService>? logger = null)
    {
        _logger = logger ?? NullLogger<Sha256FileHashService>.Instance;
    }

    public async Task<FileHashResult> ComputeHashAsync(
        FileHashRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FilePath);

        if (!Path.IsPathRooted(request.FilePath))
        {
            throw new ArgumentException("The file path must be absolute.", nameof(request.FilePath));
        }

        EnsureSupportedAlgorithm(request.Algorithm);

        var filePath = Path.GetFullPath(request.FilePath);
        var correlationId = ResolveCorrelationId(request.CorrelationId);
        var algorithmName = GetAlgorithmName(request.Algorithm);
        var fileExtension = Path.GetExtension(filePath);
        var startedAtUtc = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "File hash operation started. operation={Operation} correlation_id={CorrelationId} algorithm={Algorithm} file_extension={FileExtension}",
            HashOperationName,
            correlationId,
            algorithmName,
            fileExtension);

        try
        {
            await using var stream = await OpenReadStreamAsync(filePath, cancellationToken).ConfigureAwait(false);
            var fileSizeBytes = stream.Length;

            var hashBytes = await ComputeHashBytesAsync(stream, request.Algorithm, cancellationToken).ConfigureAwait(false);
            var hexDigest = Convert.ToHexString(hashBytes).ToLowerInvariant();
            var completedAtUtc = DateTimeOffset.UtcNow;
            var duration = completedAtUtc - startedAtUtc;

            _logger.LogInformation(
                "File hash operation completed. operation={Operation} correlation_id={CorrelationId} algorithm={Algorithm} file_size_bytes={FileSizeBytes} duration_ms={DurationMs} hash_prefix={HashPrefix}",
                HashOperationName,
                correlationId,
                algorithmName,
                fileSizeBytes,
                duration.TotalMilliseconds,
                GetHashPrefix(hexDigest));

            return new FileHashResult
            {
                CorrelationId = correlationId,
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Algorithm = request.Algorithm,
                HexDigest = hexDigest,
                FileSizeBytes = fileSizeBytes,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                Duration = duration
            };
        }
        catch (FileNotFoundException exception)
        {
            LogHashFailure(exception, correlationId, algorithmName, fileExtension, startedAtUtc);
            throw new FileNotFoundException("The file to hash was not found.");
        }
        catch (DirectoryNotFoundException exception)
        {
            LogHashFailure(exception, correlationId, algorithmName, fileExtension, startedAtUtc);
            throw new DirectoryNotFoundException("The directory for the file to hash was not found.");
        }
        catch (Exception exception)
        {
            LogHashFailure(exception, correlationId, algorithmName, fileExtension, startedAtUtc);
            throw;
        }
    }

    public async Task<string> WriteSha256FileAsync(
        FileHashResult result,
        string targetFolderPath,
        string outputFileName = DefaultOutputFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFileName);

        if (!Path.IsPathRooted(targetFolderPath))
        {
            throw new ArgumentException("The target folder path must be absolute.", nameof(targetFolderPath));
        }

        EnsureSupportedAlgorithm(result.Algorithm);

        var fullTargetFolderPath = Path.GetFullPath(targetFolderPath);
        Directory.CreateDirectory(fullTargetFolderPath);

        var safeOutputFileName = SafePathName.Create(outputFileName, nameof(outputFileName)).Value;
        var outputPath = SafePathName.ResolvePathWithinRoot(fullTargetFolderPath, safeOutputFileName);
        var content = BuildSha256FileContent(result);

        await File.WriteAllTextAsync(outputPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Hash file written. operation={Operation} correlation_id={CorrelationId} algorithm={Algorithm} file_size_bytes={FileSizeBytes} output_file_name={OutputFileName} hash_prefix={HashPrefix}",
            SidecarWriteOperationName,
            result.CorrelationId,
            GetAlgorithmName(result.Algorithm),
            result.FileSizeBytes,
            safeOutputFileName,
            GetHashPrefix(result.HexDigest));

        return outputPath;
    }

    protected virtual Task<Stream> OpenReadStreamAsync(string filePath, CancellationToken cancellationToken)
    {
        Stream stream = new FileStream(
            filePath,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

        return Task.FromResult(stream);
    }

    private static async Task<byte[]> ComputeHashBytesAsync(
        Stream stream,
        FileHashAlgorithm algorithm,
        CancellationToken cancellationToken)
    {
        EnsureSupportedAlgorithm(algorithm);

        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSizeBytes);

        try
        {
            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, DefaultBufferSizeBytes), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                incrementalHash.AppendData(buffer, 0, bytesRead);
            }

            return incrementalHash.GetHashAndReset();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private void LogHashFailure(
        Exception exception,
        string correlationId,
        string algorithmName,
        string fileExtension,
        DateTimeOffset startedAtUtc)
    {
        var duration = DateTimeOffset.UtcNow - startedAtUtc;

        _logger.LogError(
            exception,
            "File hash operation failed. operation={Operation} correlation_id={CorrelationId} algorithm={Algorithm} file_extension={FileExtension} duration_ms={DurationMs} failure_type={FailureType}",
            HashOperationName,
            correlationId,
            algorithmName,
            fileExtension,
            duration.TotalMilliseconds,
            exception.GetType().Name);
    }

    private static void EnsureSupportedAlgorithm(FileHashAlgorithm algorithm)
    {
        if (algorithm != FileHashAlgorithm.Sha256)
        {
            throw new NotSupportedException($"The file hash algorithm '{algorithm}' is not supported.");
        }
    }

    private static string GetAlgorithmName(FileHashAlgorithm algorithm)
    {
        return algorithm switch
        {
            FileHashAlgorithm.Sha256 => "SHA-256",
            _ => throw new NotSupportedException($"The file hash algorithm '{algorithm}' is not supported.")
        };
    }

    private static string BuildSha256FileContent(FileHashResult result)
    {
        return string.Join(
                   "\n",
                   [
                       $"algorithm: {GetAlgorithmName(result.Algorithm)}",
                       $"file_name: {result.FileName}",
                       $"file_size_bytes: {result.FileSizeBytes}",
                       $"sha256: {result.HexDigest}"
                   ]) + "\n";
    }

    private static string GetHashPrefix(string hexDigest)
    {
        return hexDigest.Length <= 12
            ? hexDigest
            : hexDigest[..12];
    }

    private static string ResolveCorrelationId(string? correlationId)
    {
        return string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId.Trim();
    }
}
