using System.Collections;
using System.Reflection;
using DumpLens.Application.Cases;
using DumpLens.Application.Sources;
using DumpLens.Application.SourceReferences;

namespace DumpLens.Tests.Unit.UI;

public sealed class SourceManagerViewModelTests
{
    [Fact]
    public void SourceManagerViewModel_No_Active_Case_Shows_Empty_State_And_Does_Not_Load()
    {
        var logs = new List<UiLogEntry>();
        var service = new FakeSourceManagerService();
        var viewModel = CreateViewModel(activeCase: null, service, logs);

        Assert.False(GetBooleanProperty(viewModel, "HasActiveCase"));
        Assert.True(GetBooleanProperty(viewModel, "IsEmptyStateVisible"));
        Assert.Equal("Create or open a case to view imported sources.", GetStringProperty(viewModel, "EmptyStateMessage"));
        Assert.Equal("Create or open a case to view imported sources.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.Equal(0, GetIntProperty(viewModel, "SourceCount"));
        Assert.Equal(0, service.SummariesCallCount);

        var detail = GetPropertyValue(viewModel, "CurrentDetail");
        Assert.Equal("Create or open a case to inspect safe source references.", GetStringProperty(detail, "Description"));

        Assert.Contains(logs, entry => entry.Operation == "source_manager_opened");
        Assert.Contains(logs, entry => entry.Operation == "source_manager_active_case_missing");
    }

    [Fact]
    public async Task SourceManagerViewModel_Active_Case_Loads_Summaries_And_First_Detail()
    {
        var logs = new List<UiLogEntry>();
        var sourceReferenceReader = new FakeSourceReferenceReader
        {
            Details =
            {
                ["src-002"] = CreateSourceReferenceDetail("src-002", "Synthetic Messages B"),
                ["src-001"] = CreateSourceReferenceDetail("src-001", "Synthetic Messages A")
            }
        };
        var service = new FakeSourceManagerService
        {
            Summaries =
            [
                CreateSummary("src-002", "Synthetic Messages B", 20, 3, "bbb222bbb222bbb222bbb222"),
                CreateSummary("src-001", "Synthetic Messages A", 10, 1, "aaa111aaa111aaa111aaa111")
            ]
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs, sourceReferenceReader);

        await WaitForAsync(() => GetIntProperty(viewModel, "SourceCount") == 2);
        await WaitForAsync(() =>
        {
            var detail = GetPropertyValue(viewModel, "CurrentDetail");
            return string.Equals(GetFieldValue(detail, "Source Reference", "Source Import ID"), "src-002", StringComparison.Ordinal);
        });

        Assert.Equal("2 sources loaded.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.Equal(30, GetIntProperty(viewModel, "TotalRecordCount"));
        Assert.Equal(4, GetIntProperty(viewModel, "TotalWarningCount"));
        Assert.Equal(1, service.SummariesCallCount);
        Assert.Equal(1, sourceReferenceReader.CallCount);

        var selectedSource = GetPropertyValue(viewModel, "SelectedSource");
        Assert.Equal("src-002", GetStringProperty(selectedSource, "SourceImportId"));

        var detail = GetPropertyValue(viewModel, "CurrentDetail");
        Assert.Equal("Synthetic Messages B", GetFieldValue(detail, "Source Reference", "Source Name"));
        Assert.Equal("src-002.csv", GetFieldValue(detail, "Source Reference", "Original Filename"));
        Assert.Contains(logs, entry => entry.Operation == "source_manager_source_list_load_succeeded");
        Assert.Contains(logs, entry => entry.Operation == "source_manager_source_selected");
        Assert.Contains(logs, entry => entry.Operation == "source_reference_inspector_loaded");
    }

    [Fact]
    public async Task SourceManagerViewModel_Selecting_A_Different_Source_Updates_Detail()
    {
        var logs = new List<UiLogEntry>();
        var sourceReferenceReader = new FakeSourceReferenceReader
        {
            Details =
            {
                ["src-002"] = CreateSourceReferenceDetail("src-002", "Synthetic Messages B"),
                ["src-001"] = CreateSourceReferenceDetail("src-001", "Synthetic Messages A")
            }
        };
        var service = new FakeSourceManagerService
        {
            Summaries =
            [
                CreateSummary("src-002", "Synthetic Messages B", 20, 3, "bbb222bbb222bbb222bbb222"),
                CreateSummary("src-001", "Synthetic Messages A", 10, 1, "aaa111aaa111aaa111aaa111")
            ]
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs, sourceReferenceReader);
        await WaitForAsync(() => GetIntProperty(viewModel, "SourceCount") == 2);

        var sources = GetCollection(viewModel, "Sources");
        SetPropertyValue(viewModel, "SelectedSource", sources[1]);

        await WaitForAsync(() =>
        {
            var detail = GetPropertyValue(viewModel, "CurrentDetail");
            return string.Equals(GetFieldValue(detail, "Source Reference", "Source Import ID"), "src-001", StringComparison.Ordinal);
        });

        var detail = GetPropertyValue(viewModel, "CurrentDetail");
        Assert.Equal("Synthetic Messages A", GetFieldValue(detail, "Source Reference", "Source Name"));
        Assert.Equal(2, sourceReferenceReader.CallCount);
        Assert.Equal(2, logs.Count(entry => entry.Operation == "source_manager_source_selected"));
    }

    [Fact]
    public async Task SourceManagerViewModel_Load_Failure_Shows_Safe_Error()
    {
        const string sensitiveToken = "TOP_SECRET_EVIDENCE_TOKEN";
        var logs = new List<UiLogEntry>();
        var service = new FakeSourceManagerService
        {
            SummariesException = new InvalidOperationException(sensitiveToken)
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs);

        await WaitForAsync(() => GetBooleanProperty(viewModel, "HasError"));

        Assert.Equal("Sources could not be loaded. Check the case package and try again.", GetStringProperty(viewModel, "ErrorMessage"));
        Assert.Equal("Sources could not be loaded. Check the case package and try again.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.Equal(0, GetIntProperty(viewModel, "SourceCount"));

        Assert.Contains(logs, entry => entry.Operation == "source_manager_source_list_load_failed");
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, entry.Message, StringComparison.Ordinal));
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, string.Join("|", entry.Fields.Values), StringComparison.Ordinal));
    }

    [Fact]
    public async Task SourceManagerViewModel_Logs_Do_Not_Include_Sensitive_Source_Values()
    {
        const string sensitiveToken = "TOP_SECRET_PHONE_8035551234";
        var logs = new List<UiLogEntry>();
        var sourceReferenceReader = new FakeSourceReferenceReader
        {
            Details =
            {
                ["src-sensitive"] = CreateSourceReferenceDetail("src-sensitive", "Synthetic Sensitive Source")
            }
        };
        var service = new FakeSourceManagerService
        {
            Summaries =
            [
                CreateSummary("src-sensitive", sensitiveToken, 12, 2, "abcdefabcdefabcdefabcdef")
            ]
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs, sourceReferenceReader);
        await WaitForAsync(() =>
        {
            var detail = GetPropertyValue(viewModel, "CurrentDetail");
            return string.Equals(GetFieldValue(detail, "Source Reference", "Source Import ID"), "src-sensitive", StringComparison.Ordinal);
        });

        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, entry.Message, StringComparison.Ordinal));
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, string.Join("|", entry.Fields.Values), StringComparison.Ordinal));
    }

    private static object CreateViewModel(
        CreateCaseResult? activeCase,
        ISourceManagerService service,
        List<UiLogEntry> logs,
        ISourceReferenceReader? sourceReferenceReader = null)
    {
        var assembly = ViewModelAssemblyLoader.Load();
        var type = assembly.GetType("DumpLens.App.ViewModels.SourceManagerViewModel", throwOnError: true)!;
        Action<string, string, string, IReadOnlyDictionary<string, string>?> logAction =
            (operation, correlationId, message, fields) =>
            {
                logs.Add(new UiLogEntry(operation, correlationId, message, fields ?? new Dictionary<string, string>(StringComparer.Ordinal)));
            };

        return Activator.CreateInstance(type, activeCase, service, sourceReferenceReader ?? new FakeSourceReferenceReader(), logAction)!;
    }

    private static CreateCaseResult CreateActiveCase()
    {
        return new CreateCaseResult
        {
            CaseId = "case-src-001",
            PackageId = "pkg-src-001",
            CaseNumber = "DL-SRC-001",
            Title = "Synthetic Source Case",
            PackageRootPath = @"O:\Cases\SyntheticSourceCase",
            DatabasePath = @"O:\Cases\SyntheticSourceCase\case.dlensdb",
            ManifestPath = @"O:\Cases\SyntheticSourceCase\manifest.json",
            CreatedAtUtc = DateTimeOffset.Parse("2026-04-28T12:00:00Z"),
            CorrelationId = "case-src-load"
        };
    }

    private static SourceImportSummary CreateSummary(
        string sourceImportId,
        string sourceName,
        int recordCount,
        int warningCount,
        string hash)
    {
        return new SourceImportSummary
        {
            SourceImportId = sourceImportId,
            SourceName = sourceName,
            SourceType = "csv_messages",
            Platform = "sms",
            ImportStatus = "imported",
            RecordCount = recordCount,
            WarningCount = warningCount,
            ImportedAtUtc = DateTimeOffset.Parse("2026-04-28T12:00:00Z"),
            OriginalFilename = $"{sourceImportId}.csv",
            FileSizeBytes = 1024,
            FileSha256 = hash
        };
    }

    private static SourceReferenceDetail CreateSourceReferenceDetail(
        string sourceImportId,
        string sourceName)
    {
        return new SourceReferenceDetail
        {
            CaseId = "case-src-001",
            SourceImportId = sourceImportId,
            SourceName = sourceName,
            SourceType = "csv_messages",
            Platform = "sms",
            ImportStatus = "imported",
            OriginalFilename = $"{sourceImportId}.csv",
            StoredRelativePath = $"imports/source_{sourceImportId}/original/{sourceImportId}.csv",
            FileSizeBytes = 2048,
            FileSha256 = "abcdef123456abcdef123456abcdef123456abcdef123456abcdef123456abcd",
            ImportedAtUtc = DateTimeOffset.Parse("2026-04-28T12:00:00Z"),
            HasSourceMetadata = true,
            WasArtifactReferenceRequested = false,
            WasMessageReferenceRequested = false
        };
    }

    private static List<object> GetCollection(object instance, string propertyName)
    {
        var enumerable = Assert.IsAssignableFrom<IEnumerable>(GetPropertyValue(instance, propertyName));
        return enumerable.Cast<object>().ToList();
    }

    private static bool GetBooleanProperty(object instance, string propertyName)
    {
        return Assert.IsType<bool>(GetPropertyValue(instance, propertyName));
    }

    private static int GetIntProperty(object instance, string propertyName)
    {
        if (propertyName.Contains('.', StringComparison.Ordinal))
        {
            return Assert.IsType<int>(GetNestedPropertyValue(instance, propertyName));
        }

        return Assert.IsType<int>(GetPropertyValue(instance, propertyName));
    }

    private static object GetPropertyValue(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        var value = property!.GetValue(instance);
        Assert.NotNull(value);
        return value!;
    }

    private static string GetStringProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return Assert.IsType<string>(value);
    }

    private static object GetNestedPropertyValue(object instance, string propertyPath)
    {
        var current = instance;
        foreach (var segment in propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            current = GetPropertyValue(current, segment);
        }

        return current;
    }

    private static string GetFieldValue(object inspector, string sectionTitle, string fieldLabel)
    {
        var sections = GetCollection(inspector, "Sections");
        var section = sections.Single(item => string.Equals(GetStringProperty(item, "Title"), sectionTitle, StringComparison.Ordinal));
        var fields = GetCollection(section, "Fields");
        var field = fields.Single(item => string.Equals(GetStringProperty(item, "Label"), fieldLabel, StringComparison.Ordinal));
        return GetStringProperty(field, "Value");
    }

    private static void SetPropertyValue(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - startedAt).TotalMilliseconds > timeoutMilliseconds)
            {
                throw new TimeoutException("Timed out waiting for the source manager state to update.");
            }

            await Task.Delay(25);
        }
    }

    private sealed record UiLogEntry(
        string Operation,
        string CorrelationId,
        string Message,
        IReadOnlyDictionary<string, string> Fields);

    private sealed class FakeSourceManagerService : ISourceManagerService
    {
        public IReadOnlyList<SourceImportSummary> Summaries { get; init; } = Array.Empty<SourceImportSummary>();

        public Exception? SummariesException { get; init; }

        public int SummariesCallCount { get; private set; }

        public Task<SourceImportDetail?> GetDetailAsync(
            LoadSourceImportDetailRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Source detail loading is not used by the shared source reference inspector.");
        }

        public Task<IReadOnlyList<SourceImportSummary>> GetSummariesAsync(
            LoadSourceImportSummariesRequest request,
            CancellationToken cancellationToken = default)
        {
            SummariesCallCount++;
            if (SummariesException is not null)
            {
                throw SummariesException;
            }

            return Task.FromResult(Summaries);
        }
    }

    private sealed class FakeSourceReferenceReader : ISourceReferenceReader
    {
        public int CallCount { get; private set; }

        public Dictionary<string, SourceReferenceDetail?> Details { get; init; } = new(StringComparer.Ordinal);

        public Exception? Exception { get; init; }

        public Task<SourceReferenceDetail?> LoadAsync(
            LoadSourceReferenceRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Exception is not null)
            {
                throw Exception;
            }

            Details.TryGetValue(request.SourceImportId, out var detail);
            return Task.FromResult(detail);
        }
    }
}
