using System.Text.Json;
using DumpLens.Application.CasePackages;
using DumpLens.Persistence.CasePackages;
using Microsoft.Extensions.Logging;

namespace DumpLens.Tests.Integration.CasePackages;

public sealed class CasePackageServiceTests
{
    private static readonly Dictionary<string, string> ExpectedFolders = new(StringComparer.Ordinal)
    {
        ["imports"] = "imports",
        ["indexes"] = "indexes",
        ["attachments"] = "attachments",
        ["attachments_thumbnails"] = "attachments/thumbnails",
        ["attachments_extracted_text"] = "attachments/extracted_text",
        ["attachments_media_cache"] = "attachments/media_cache",
        ["reports"] = "reports",
        ["exports"] = "exports",
        ["logs"] = "logs",
        ["backups"] = "backups"
    };

    [Fact]
    public async Task CreateAsync_CreatesTheStandardCasePackageLayoutAndManifest()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var service = new CasePackageService();

        var result = await service.CreateAsync(new CasePackageCreateRequest
        {
            RootDirectoryPath = tempDirectory.DirectoryPath,
            CaseId = "case-001",
            CaseNumber = "DL-001",
            Title = "Synthetic Case",
            RequestedFolderName = "Case<> Package"
        });

        Assert.True(Directory.Exists(result.PackageRootPath));
        Assert.Equal(Path.Combine(tempDirectory.DirectoryPath, "Case- Package"), result.PackageRootPath);
        Assert.Equal(Path.Combine(result.PackageRootPath, "case.dlensdb"), result.DatabasePath);
        Assert.Equal("case.dlensdb", result.DatabaseRelativePath);
        Assert.False(File.Exists(result.DatabasePath));
        Assert.True(File.Exists(result.ManifestPath));

        foreach (var expectedFolder in ExpectedFolders)
        {
            Assert.True(result.Folders.ContainsKey(expectedFolder.Key));
            Assert.Equal(expectedFolder.Value, result.Folders[expectedFolder.Key]);
            Assert.True(Directory.Exists(Path.Combine(result.PackageRootPath, expectedFolder.Value)));
        }

        await using var manifestStream = File.OpenRead(result.ManifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<CasePackageManifest>(manifestStream);

        Assert.NotNull(manifest);
        Assert.Equal("1", manifest.PackageVersion);
        Assert.Equal(result.PackageId, manifest.PackageId);
        Assert.Equal("case-001", manifest.CaseId);
        Assert.Equal("DL-001", manifest.CaseNumber);
        Assert.Equal("Synthetic Case", manifest.Title);
        Assert.Equal("DumpLens", manifest.AppName);
        Assert.Equal("case.dlensdb", manifest.DatabaseRelativePath);
        Assert.Equal("copy", manifest.PreparationMode);
        Assert.Equal(ExpectedFolders, manifest.Folders);
        Assert.True(DateTimeOffset.TryParse(manifest.CreatedAtUtc, out _));
    }

    [Fact]
    public async Task CreateAsync_FailsClearlyWhenTheTargetPackageAlreadyExists()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var service = new CasePackageService();
        var request = new CasePackageCreateRequest
        {
            RootDirectoryPath = tempDirectory.DirectoryPath,
            CaseId = "case-001",
            RequestedFolderName = "Repeat Package"
        };

        await service.CreateAsync(request);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request));

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_EmitsEvidenceSafeStructuredLogs()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var logger = new TestLogger<CasePackageService>();
        var service = new CasePackageService(logger);
        const string sensitiveTitle = "TOP_SECRET_EVIDENCE_TITLE";

        await service.CreateAsync(new CasePackageCreateRequest
        {
            RootDirectoryPath = tempDirectory.DirectoryPath,
            CaseId = "case-logging",
            Title = sensitiveTitle,
            RequestedFolderName = "..\\Sensitive<>Folder"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(new CasePackageCreateRequest
            {
                RootDirectoryPath = tempDirectory.DirectoryPath,
                CaseId = "case-logging",
                Title = sensitiveTitle,
                RequestedFolderName = "..\\Sensitive<>Folder"
            }));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Case package creation started.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Case package directory created.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Manifest written.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Case package creation completed.", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Case package creation failed.", StringComparison.Ordinal));
        Assert.All(logger.Entries, entry => Assert.DoesNotContain(sensitiveTitle, entry.Message, StringComparison.Ordinal));
        Assert.All(
            logger.Entries,
            entry =>
            {
                Assert.True(entry.State.ContainsKey("Operation"));
                Assert.True(entry.State.ContainsKey("CorrelationId"));
                Assert.True(entry.State.ContainsKey("PackageId"));
                Assert.True(entry.State.ContainsKey("CaseId"));
            });
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
}
