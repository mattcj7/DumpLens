using System.Security.Cryptography;
using System.Text;
using DumpLens.Application.FileHashing;
using DumpLens.Security.FileHashing;
using Microsoft.Extensions.Logging;

namespace DumpLens.Tests.Unit.Security.FileHashing;

public sealed class Sha256FileHashServiceTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("DumpLens", "51bf2b257ddf83340135bdddf107da20e7353b7cbdf344ad35998282f1535187")]
    public async Task ComputeHashAsync_ComputesKnownSha256Values(string content, string expectedDigest)
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "fixture.txt");
        await File.WriteAllTextAsync(filePath, content, Utf8NoBom);

        var service = new Sha256FileHashService();

        var result = await service.ComputeHashAsync(new FileHashRequest
        {
            FilePath = filePath
        });

        Assert.Equal(FileHashAlgorithm.Sha256, result.Algorithm);
        Assert.Equal(expectedDigest, result.HexDigest);
        Assert.Equal(new FileInfo(filePath).Length, result.FileSizeBytes);
        Assert.Equal(Path.GetFullPath(filePath), result.FilePath);
        Assert.Equal("fixture.txt", result.FileName);
        Assert.True(result.CompletedAtUtc >= result.StartedAtUtc);
        Assert.True(result.Duration >= TimeSpan.Zero);
        Assert.False(string.IsNullOrWhiteSpace(result.CorrelationId));
    }

    [Fact]
    public async Task ComputeHashAsync_StreamsTheInputAcrossMultipleReads()
    {
        var payload = new byte[220000];
        RandomNumberGenerator.Fill(payload);

        var trackingStream = new TrackingReadStream(payload);
        var service = new TrackingSha256FileHashService(trackingStream);

        var result = await service.ComputeHashAsync(new FileHashRequest
        {
            FilePath = Path.Combine(Path.GetTempPath(), "DumpLens.Hashing", "synthetic.bin"),
            CorrelationId = "stream-test"
        });

        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), result.HexDigest);
        Assert.Equal(payload.Length, result.FileSizeBytes);
        Assert.True(trackingStream.ReadCallCount >= 3);
        Assert.True(trackingStream.MaxRequestedCount <= 81920);
    }

    [Fact]
    public async Task ComputeHashAsync_FailsWithSafeMessageWhenFileIsMissing()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var service = new Sha256FileHashService();
        var missingFilePath = Path.Combine(tempDirectory.DirectoryPath, "missing.txt");

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => service.ComputeHashAsync(new FileHashRequest
        {
            FilePath = missingFilePath
        }));

        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(missingFilePath, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteSha256FileAsync_WritesStableHumanReadableContent()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "artifact.txt");
        await File.WriteAllTextAsync(filePath, "abc", Utf8NoBom);

        var service = new Sha256FileHashService();
        var result = await service.ComputeHashAsync(new FileHashRequest
        {
            FilePath = filePath,
            CorrelationId = "write-test"
        });

        var outputPath = await service.WriteSha256FileAsync(result, tempDirectory.DirectoryPath);
        var content = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);

        Assert.Equal(Path.Combine(tempDirectory.DirectoryPath, "sha256.txt"), outputPath);
        Assert.Equal(
            "algorithm: SHA-256\nfile_name: artifact.txt\nfile_size_bytes: 3\nsha256: ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\n",
            content);
    }

    [Fact]
    public async Task WriteSha256FileAsync_RejectsRelativeTargetFolderPaths()
    {
        var service = new Sha256FileHashService();
        var result = new FileHashResult
        {
            CorrelationId = "relative-target",
            FilePath = Path.Combine(Path.GetTempPath(), "artifact.txt"),
            FileName = "artifact.txt",
            Algorithm = FileHashAlgorithm.Sha256,
            HexDigest = "abc123",
            FileSizeBytes = 3,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Duration = TimeSpan.Zero
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.WriteSha256FileAsync(result, "relative-folder"));

        Assert.Equal("targetFolderPath", exception.ParamName);
    }

    [Fact]
    public async Task HashOperations_EmitEvidenceSafeStructuredLogs()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var logger = new TestLogger<Sha256FileHashService>();
        var service = new Sha256FileHashService(logger);
        const string sensitiveContent = "TOP_SECRET_EVIDENCE_BODY";
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "evidence.txt");
        await File.WriteAllTextAsync(filePath, sensitiveContent, Utf8NoBom);

        var result = await service.ComputeHashAsync(new FileHashRequest
        {
            FilePath = filePath,
            CorrelationId = "log-test"
        });

        await service.WriteSha256FileAsync(result, tempDirectory.DirectoryPath);

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.ComputeHashAsync(new FileHashRequest
        {
            FilePath = Path.Combine(tempDirectory.DirectoryPath, "missing.txt"),
            CorrelationId = "log-failure"
        }));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("File hash operation started.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("File hash operation completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Hash file written.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("File hash operation failed.", StringComparison.Ordinal));
        Assert.All(logger.Entries, entry => Assert.DoesNotContain(sensitiveContent, entry.Message, StringComparison.Ordinal));
        Assert.All(logger.Entries, entry => Assert.True(entry.State.ContainsKey("Operation")));
        Assert.All(logger.Entries, entry => Assert.True(entry.State.ContainsKey("CorrelationId")));
        Assert.All(logger.Entries, entry => Assert.True(entry.State.ContainsKey("Algorithm")));
    }

    private sealed class TrackingSha256FileHashService : Sha256FileHashService
    {
        private readonly TrackingReadStream _stream;

        public TrackingSha256FileHashService(TrackingReadStream stream)
        {
            _stream = stream;
        }

        protected override Task<Stream> OpenReadStreamAsync(string filePath, CancellationToken cancellationToken)
        {
            _stream.Position = 0;
            return Task.FromResult<Stream>(_stream);
        }
    }

    private sealed class TrackingReadStream : Stream
    {
        private readonly MemoryStream _innerStream;

        public TrackingReadStream(byte[] payload)
        {
            _innerStream = new MemoryStream(payload, writable: false);
        }

        public int ReadCallCount { get; private set; }

        public int MaxRequestedCount { get; private set; }

        public override bool CanRead => _innerStream.CanRead;

        public override bool CanSeek => _innerStream.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _innerStream.Length;

        public override long Position
        {
            get => _innerStream.Position;
            set => _innerStream.Position = value;
        }

        public override void Flush()
        {
            _innerStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            MaxRequestedCount = Math.Max(MaxRequestedCount, count);
            ReadCallCount++;
            return _innerStream.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            MaxRequestedCount = Math.Max(MaxRequestedCount, buffer.Length);
            ReadCallCount++;
            return _innerStream.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            MaxRequestedCount = Math.Max(MaxRequestedCount, buffer.Length);
            ReadCallCount++;
            return _innerStream.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _innerStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _innerStream.Dispose();
            }

            base.Dispose(disposing);
        }
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

            Entries.Add(new LogEntry(logLevel, formatter(state, exception), structuredState));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> State);

    private sealed class NoOpScope : IDisposable
    {
        public static NoOpScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class TemporaryDirectoryScope : IDisposable
    {
        private TemporaryDirectoryScope(string directoryPath)
        {
            DirectoryPath = directoryPath;
        }

        public string DirectoryPath { get; }

        public static TemporaryDirectoryScope Create()
        {
            var directoryPath = Path.Combine(Path.GetTempPath(), "DumpLens.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directoryPath);
            return new TemporaryDirectoryScope(directoryPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
