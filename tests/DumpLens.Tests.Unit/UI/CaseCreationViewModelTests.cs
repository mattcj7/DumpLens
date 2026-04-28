using System.ComponentModel;
using System.Reflection;
using DumpLens.Application.Cases;

namespace DumpLens.Tests.Unit.UI;

public class CaseCreationViewModelTests
{
    [Fact]
    public async Task CaseCreationViewModel_Rejects_Missing_Title()
    {
        using var parentDirectory = new TemporaryDirectory();
        var fakeService = new FakeCaseService();
        var logs = new List<UiLogEntry>();
        var viewModel = CreateViewModel(fakeService, logs);

        SetPropertyValue(viewModel, "ParentDirectoryPath", parentDirectory.Path);
        SetPropertyValue(viewModel, "Title", null);

        await SubmitAsync(viewModel);

        Assert.True(GetBooleanProperty(viewModel, "HasErrors"));
        Assert.Contains("Title is required.", GetErrors(viewModel, "Title"));
        Assert.Equal("Fix the highlighted fields and try again.", GetStringProperty(viewModel, "GeneralErrorMessage", allowEmpty: false));
        Assert.Equal(0, fakeService.CallCount);
    }

    [Fact]
    public async Task CaseCreationViewModel_Rejects_Missing_Parent_Directory()
    {
        var fakeService = new FakeCaseService();
        var logs = new List<UiLogEntry>();
        var viewModel = CreateViewModel(fakeService, logs);

        SetPropertyValue(viewModel, "Title", "Synthetic Case");
        SetPropertyValue(viewModel, "ParentDirectoryPath", null);

        await SubmitAsync(viewModel);

        Assert.True(GetBooleanProperty(viewModel, "HasErrors"));
        Assert.Contains("Parent/root directory is required.", GetErrors(viewModel, "ParentDirectoryPath"));
        Assert.Equal(0, fakeService.CallCount);
    }

    [Fact]
    public void CaseCreationViewModel_Rejects_End_Before_Start()
    {
        using var parentDirectory = new TemporaryDirectory();
        var fakeService = new FakeCaseService();
        var logs = new List<UiLogEntry>();
        var viewModel = CreateViewModel(fakeService, logs);

        SetPropertyValue(viewModel, "Title", "Synthetic Case");
        SetPropertyValue(viewModel, "ParentDirectoryPath", parentDirectory.Path);
        SetPropertyValue(viewModel, "Timezone", "Eastern Standard Time");
        SetPropertyValue(viewModel, "IncidentStartText", "2026-04-28 14:00");
        SetPropertyValue(viewModel, "IncidentEndText", "2026-04-28 13:00");

        Assert.Contains("Incident end cannot be before incident start.", GetErrors(viewModel, "IncidentEndText"));
    }

    [Fact]
    public async Task CaseCreationViewModel_Submit_Succeeds_And_Calls_Service()
    {
        using var parentDirectory = new TemporaryDirectory();
        var fakeService = new FakeCaseService
        {
            ResultFactory = request => new CreateCaseResult
            {
                CaseId = "case_001",
                PackageId = "pkg_001",
                CaseNumber = request.CaseNumber,
                Title = request.Title ?? "Synthetic Case",
                PackageRootPath = Path.Combine(parentDirectory.Path, "SyntheticCase"),
                DatabasePath = Path.Combine(parentDirectory.Path, "SyntheticCase", "case.dlensdb"),
                ManifestPath = Path.Combine(parentDirectory.Path, "SyntheticCase", "manifest.json"),
                CreatedAtUtc = DateTimeOffset.Parse("2026-04-28T17:45:00Z"),
                AuditEventId = "audit_001",
                CorrelationId = request.CorrelationId ?? "corr_001"
            }
        };

        var logs = new List<UiLogEntry>();
        CreateCaseResult? successResult = null;
        var viewModel = CreateViewModel(fakeService, logs, result => successResult = result);

        SetPropertyValue(viewModel, "CaseNumber", "DL-001");
        SetPropertyValue(viewModel, "Title", "Synthetic Case");
        SetPropertyValue(viewModel, "IncidentType", "Phone Dump Review");
        SetPropertyValue(viewModel, "IncidentStartText", "2026-04-28 13:45");
        SetPropertyValue(viewModel, "IncidentEndText", "2026-04-28 15:15");
        SetPropertyValue(viewModel, "Timezone", "Eastern Standard Time");
        SetPropertyValue(viewModel, "ParentDirectoryPath", parentDirectory.Path);
        SetPropertyValue(viewModel, "RequestedPackageFolderName", "SyntheticCase");

        await SubmitAsync(viewModel);

        Assert.Equal(1, fakeService.CallCount);
        Assert.NotNull(fakeService.LastRequest);
        Assert.Equal("DL-001", fakeService.LastRequest!.CaseNumber);
        Assert.Equal("Synthetic Case", fakeService.LastRequest.Title);
        Assert.Equal("Phone Dump Review", fakeService.LastRequest.IncidentType);
        Assert.Equal(DateTimeOffset.Parse("2026-04-28T17:45:00+00:00"), fakeService.LastRequest.IncidentStartUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-04-28T19:15:00+00:00"), fakeService.LastRequest.IncidentEndUtc);
        Assert.Equal(parentDirectory.Path, fakeService.LastRequest.ParentDirectoryPath);
        Assert.Equal("SyntheticCase", fakeService.LastRequest.RequestedPackageFolderName);
        Assert.NotNull(successResult);
        Assert.Equal("Synthetic Case", successResult!.Title);
        Assert.DoesNotContain(logs, static entry => entry.Operation == "case_creation_ui_failed");
        Assert.Contains(logs, static entry => entry.Operation == "case_creation_ui_submitted");
        Assert.Contains(logs, static entry => entry.Operation == "case_creation_ui_succeeded");
        Assert.Null(GetNullableStringProperty(viewModel, "GeneralErrorMessage"));
    }

    [Fact]
    public async Task CaseCreationViewModel_Submit_Failure_Shows_Safe_Error()
    {
        using var parentDirectory = new TemporaryDirectory();
        var fakeService = new FakeCaseService
        {
            Failure = new InvalidOperationException("Raw exception dump should stay out of the UI.")
        };

        var logs = new List<UiLogEntry>();
        CreateCaseResult? successResult = null;
        var viewModel = CreateViewModel(fakeService, logs, result => successResult = result);

        SetPropertyValue(viewModel, "Title", "Synthetic Case");
        SetPropertyValue(viewModel, "ParentDirectoryPath", parentDirectory.Path);

        await SubmitAsync(viewModel);

        Assert.Equal(1, fakeService.CallCount);
        Assert.Null(successResult);

        var generalErrorMessage = GetStringProperty(viewModel, "GeneralErrorMessage", allowEmpty: false);
        Assert.Equal("Case could not be created. Review the form and try again.", generalErrorMessage);
        Assert.DoesNotContain("Raw exception dump", generalErrorMessage, StringComparison.Ordinal);
        Assert.Contains(logs, static entry => entry.Operation == "case_creation_ui_failed");
    }

    private static object CreateViewModel(
        ICaseService caseService,
        List<UiLogEntry> logs,
        Action<CreateCaseResult>? onSuccess = null)
    {
        var assembly = ViewModelAssemblyLoader.Load();
        var viewModelType = assembly.GetType("DumpLens.App.ViewModels.CaseCreationViewModel", throwOnError: true)!;

        Action<CreateCaseResult> successCallback = onSuccess ?? (_ => { });
        Action cancelCallback = () => { };
        Action<string, string, string, IReadOnlyDictionary<string, string>?> logAction =
            (operation, correlationId, message, fields) =>
                logs.Add(new UiLogEntry(operation, correlationId, message, fields));

        return Activator.CreateInstance(
            viewModelType,
            caseService,
            successCallback,
            cancelCallback,
            logAction,
            "Eastern Standard Time")!;
    }

    private static async Task SubmitAsync(object viewModel)
    {
        var method = viewModel.GetType().GetMethod("SubmitAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var task = method!.Invoke(viewModel, null);
        var submissionTask = Assert.IsAssignableFrom<Task>(task);
        await submissionTask;
    }

    private static IReadOnlyList<string> GetErrors(object viewModel, string propertyName)
    {
        var notifyDataErrorInfo = Assert.IsAssignableFrom<INotifyDataErrorInfo>(viewModel);
        return notifyDataErrorInfo.GetErrors(propertyName)
            .Cast<object?>()
            .Select(static value => Assert.IsType<string>(value))
            .ToList();
    }

    private static bool GetBooleanProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return Assert.IsType<bool>(value);
    }

    private static string GetStringProperty(object instance, string propertyName, bool allowEmpty)
    {
        var value = GetPropertyValue(instance, propertyName);
        var stringValue = Assert.IsType<string>(value);

        if (!allowEmpty)
        {
            Assert.False(string.IsNullOrWhiteSpace(stringValue));
        }

        return stringValue;
    }

    private static string? GetNullableStringProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return property!.GetValue(instance) as string;
    }

    private static object GetPropertyValue(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);

        var value = property!.GetValue(instance);
        Assert.NotNull(value);
        return value;
    }

    private static void SetPropertyValue(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private sealed class FakeCaseService : ICaseService
    {
        public int CallCount { get; private set; }

        public Exception? Failure { get; init; }

        public CreateCaseRequest? LastRequest { get; private set; }

        public Func<CreateCaseRequest, CreateCaseResult>? ResultFactory { get; init; }

        public Task<CreateCaseResult> CreateAsync(
            CreateCaseRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;

            if (Failure is not null)
            {
                throw Failure;
            }

            var result = ResultFactory?.Invoke(request) ?? new CreateCaseResult
            {
                CaseId = "case_default",
                PackageId = "pkg_default",
                CaseNumber = request.CaseNumber,
                Title = request.Title ?? "Synthetic Case",
                PackageRootPath = "O:\\Synthetic",
                DatabasePath = "O:\\Synthetic\\case.dlensdb",
                ManifestPath = "O:\\Synthetic\\manifest.json",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CorrelationId = request.CorrelationId ?? "corr_default"
            };

            return Task.FromResult(result);
        }
    }

    private sealed record UiLogEntry(
        string Operation,
        string CorrelationId,
        string Message,
        IReadOnlyDictionary<string, string>? Fields);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DumpLensTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
