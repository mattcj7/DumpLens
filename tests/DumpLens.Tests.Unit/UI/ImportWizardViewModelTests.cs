using System.Collections;
using System.Reflection;
using System.Windows.Input;
using DumpLens.Application.Audit;
using DumpLens.Application.CallImports;
using DumpLens.Application.Cases;
using DumpLens.Application.Imports;
using DumpLens.Application.MessageImports;
using DumpLens.Application.Sources;

namespace DumpLens.Tests.Unit.UI;

public sealed class ImportWizardViewModelTests
{
    [Fact]
    public void ImportWizardView_Xaml_Uses_OneWay_For_ReadOnly_Display_Bindings()
    {
        var xaml = File.ReadAllText(GetImportWizardViewPath());

        Assert.Contains("Text=\"{Binding StepNumber, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Title, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Description, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentStepTitle, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentStepDescription, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DetectedSourceKindText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FileSupportStatusText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ProbeDetailsText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding WarningSummaryText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SummaryText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding NextButtonText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportWizardViewModel_Initializes_On_First_Step()
    {
        var viewModel = CreatePreviewOnlyViewModel(
            [new FakeSourceImporter(ImportSourceKind.Csv)],
            out _,
            out _);

        Assert.Equal(0, GetIntProperty(viewModel, "CurrentStepIndex"));
        Assert.Equal("Choose source type", GetStringProperty(viewModel, "CurrentStepTitle"));
    }

    [Fact]
    public void ImportWizardViewModel_Source_Type_Can_Be_Selected()
    {
        var viewModel = CreatePreviewOnlyViewModel(
            [new FakeSourceImporter(ImportSourceKind.Csv), new FakeSourceImporter(ImportSourceKind.Xlsx)],
            out _,
            out _);
        var csvOption = GetCollection(viewModel, "SourceTypeOptions")
            .Single(option => string.Equals(GetStringProperty(option, "Label"), "CSV", StringComparison.Ordinal));

        SetPropertyValue(viewModel, "SelectedSourceTypeOption", csvOption);

        var selectedOption = GetPropertyValue(viewModel, "SelectedSourceTypeOption");
        Assert.Equal("CSV", GetStringProperty(selectedOption, "Label"));
    }

    [Fact]
    public async Task ImportWizardViewModel_Missing_File_Path_Blocks_Preview_With_Safe_Error()
    {
        var viewModel = CreatePreviewOnlyViewModel(
            [new FakeSourceImporter(ImportSourceKind.Csv)],
            out _,
            out _);
        SelectSourceType(viewModel, "CSV");

        await InvokeAsync(viewModel, "RefreshPreviewAsync");

        Assert.Equal("Enter an absolute file path before requesting a preview.", GetStringProperty(viewModel, "GeneralErrorMessage"));
    }

    [Fact]
    public async Task ImportWizardViewModel_Successful_Xlsx_Probe_Shows_Worksheet_Options()
    {
        var importer = new FakeSourceImporter(ImportSourceKind.Xlsx)
        {
            ProbeResultFactory = request => new ImportProbeResult
            {
                CorrelationId = request.CorrelationId ?? "probe-xlsx",
                SourceKind = ImportSourceKind.Xlsx,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                WorksheetNames = ["Messages", "Calls"],
                SelectedWorksheetName = "Messages",
                HasHeaderRow = true
            },
            PreviewResultFactory = request => new ImportPreviewResult
            {
                CorrelationId = request.CorrelationId ?? "preview-xlsx",
                SourceKind = ImportSourceKind.Xlsx,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                WorksheetNames = ["Messages", "Calls"],
                SelectedWorksheetName = request.WorksheetName ?? "Messages",
                HasHeaderRow = true,
                Columns = [new ImportPreviewColumn { Ordinal = 0, SourceColumnName = "timestamp" }],
                Rows = [new ImportPreviewRow { RowNumber = 2, Values = ["2026-04-28T10:00:00Z"] }],
                FieldMappingSuggestions =
                [
                    new ImportFieldMappingSuggestion
                    {
                        DumpLensFieldName = ImportFieldNames.Timestamp,
                        SourceColumnName = "timestamp"
                    }
                ]
            }
        };

        var viewModel = CreatePreviewOnlyViewModel([importer], out _, out _);
        SelectSourceType(viewModel, "XLSX");
        SetPropertyValue(viewModel, "FilePath", @"O:\Synthetic\preview.xlsx");

        await InvokeAsync(viewModel, "ProbeFileAsync");

        var worksheetOptions = GetCollection(viewModel, "WorksheetOptions")
            .Select(static option => Assert.IsType<string>(option))
            .ToList();

        Assert.Equal(["Messages", "Calls"], worksheetOptions);
        Assert.Equal("Messages", GetStringProperty(viewModel, "SelectedWorksheetName"));
    }

    [Fact]
    public async Task ImportWizardViewModel_Import_Blocked_When_No_Active_Case_Exists()
    {
        var sourceRegistrationService = new FakeSourceRegistrationService();
        var messageImportService = new FakeMessageImportService();
        var callImportService = new FakeCallImportService();
        var prepared = await CreatePreparedMessageImportViewModelAsync(
            activeCase: null,
            sourceRegistrationService,
            messageImportService,
            callImportService,
            importers: null,
            warningReader: null);
        var viewModel = prepared.ViewModel;
        await AdvanceToSummaryStepAsync(viewModel);

        await InvokeAsync(viewModel, "ExecuteImportAsync");

        Assert.Equal("Create or open a case before importing sources.", GetStringProperty(viewModel, "GeneralErrorMessage"));
        Assert.Equal(0, sourceRegistrationService.CallCount);
        Assert.Equal(0, messageImportService.CallCount);
        Assert.Equal(0, callImportService.CallCount);
    }

    [Fact]
    public async Task ImportWizardViewModel_Missing_Required_Message_Mapping_Blocks_Import()
    {
        var sourceRegistrationService = new FakeSourceRegistrationService();
        var messageImportService = new FakeMessageImportService();
        var callImportService = new FakeCallImportService();
        var activeCase = CreateActiveCase();
        var prepared = await CreatePreparedMessageImportViewModelAsync(
            activeCase,
            sourceRegistrationService,
            messageImportService,
            callImportService,
            importers: null,
            warningReader: null);
        var viewModel = prepared.ViewModel;

        SetMapping(viewModel, ImportFieldNames.MessageBody, "(Not mapped)");

        await InvokeAsync(viewModel, "ExecuteImportAsync");

        Assert.Equal("Map the required fields before importing messages: Message body.", GetStringProperty(viewModel, "GeneralErrorMessage"));
        Assert.Equal(0, sourceRegistrationService.CallCount);
        Assert.Equal(0, messageImportService.CallCount);
    }

    [Fact]
    public async Task ImportWizardViewModel_Message_Import_Path_Calls_Source_Registration_Then_Message_Import()
    {
        var callOrder = new List<string>();
        var activeCase = CreateActiveCase();
        var sourceRegistrationService = new FakeSourceRegistrationService
        {
            Handler = request =>
            {
                callOrder.Add("register");
                return Task.FromResult(CreateRegisteredSourceResult(activeCase, request.SourceType, request.Platform, "synthetic-message-source"));
            }
        };
        var messageImportService = new FakeMessageImportService
        {
            Handler = request =>
            {
                callOrder.Add("message");
                return Task.FromResult(new ImportMessagesResult
                {
                    CaseId = request.CaseId,
                    SourceImportId = request.SourceImportId,
                    ImportedMessageCount = 3,
                    SourceArtifactCount = 3,
                    IdentityCountCreated = 2,
                    IdentityCountReused = 1,
                    RecipientCount = 3,
                    WarningCount = 2,
                    AuditEventId = "audit-msg-001",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                    CompletedAtUtc = DateTimeOffset.UtcNow
                });
            }
        };
        var callImportService = new FakeCallImportService();
        var prepared = await CreatePreparedMessageImportViewModelAsync(
            activeCase,
            sourceRegistrationService,
            messageImportService,
            callImportService,
            importers: null,
            warningReader: new FakeImportWarningSummaryReader(
            [
                new ImportWarningSummary
                {
                    WarningCode = "missing_platform",
                    Message = "The platform value is missing for one row.",
                    Count = 2
                }
            ]));
        var viewModel = prepared.ViewModel;
        await AdvanceToSummaryStepAsync(viewModel);

        await InvokeAsync(viewModel, "ExecuteImportAsync");

        Assert.Equal(["register", "message"], callOrder);
        Assert.Equal(1, sourceRegistrationService.CallCount);
        Assert.Equal(1, messageImportService.CallCount);
        Assert.Equal(0, callImportService.CallCount);
    }

    [Fact]
    public async Task ImportWizardViewModel_Call_Import_Path_Calls_Source_Registration_Then_Call_Import()
    {
        var callOrder = new List<string>();
        var activeCase = CreateActiveCase();
        var sourceRegistrationService = new FakeSourceRegistrationService
        {
            Handler = request =>
            {
                callOrder.Add("register");
                return Task.FromResult(CreateRegisteredSourceResult(activeCase, request.SourceType, request.Platform, "synthetic-call-source"));
            }
        };
        var messageImportService = new FakeMessageImportService();
        var callImportService = new FakeCallImportService
        {
            Handler = request =>
            {
                callOrder.Add("call");
                return Task.FromResult(new ImportCallsResult
                {
                    CaseId = request.CaseId,
                    SourceImportId = request.SourceImportId,
                    ImportedCallCount = 4,
                    SourceArtifactCount = 4,
                    IdentityCountCreated = 2,
                    IdentityCountReused = 2,
                    WarningCount = 1,
                    AuditEventId = "audit-call-001",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                    CompletedAtUtc = DateTimeOffset.UtcNow
                });
            }
        };
        var prepared = await CreatePreparedCallImportViewModelAsync(
            activeCase,
            sourceRegistrationService,
            messageImportService,
            callImportService,
            importers: null,
            warningReader: null);
        var viewModel = prepared.ViewModel;
        await AdvanceToSummaryStepAsync(viewModel);

        await InvokeAsync(viewModel, "ExecuteImportAsync");

        Assert.Equal(["register", "call"], callOrder);
        Assert.Equal(1, sourceRegistrationService.CallCount);
        Assert.Equal(0, messageImportService.CallCount);
        Assert.Equal(1, callImportService.CallCount);
    }

    [Fact]
    public async Task ImportWizardViewModel_Successful_Message_Import_Summary_Contains_Counts_Hash_And_Source_Id()
    {
        var activeCase = CreateActiveCase();
        var sourceRegistrationService = new FakeSourceRegistrationService
        {
            Handler = request => Task.FromResult(CreateRegisteredSourceResult(activeCase, request.SourceType, "sms", "synthetic-message-source"))
        };
        var messageImportService = new FakeMessageImportService
        {
            Handler = request => Task.FromResult(new ImportMessagesResult
            {
                CaseId = request.CaseId,
                SourceImportId = request.SourceImportId,
                ImportedMessageCount = 3,
                SourceArtifactCount = 3,
                IdentityCountCreated = 2,
                IdentityCountReused = 1,
                RecipientCount = 3,
                WarningCount = 2,
                AuditEventId = "audit-msg-001",
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                CompletedAtUtc = DateTimeOffset.UtcNow
            })
        };
        var prepared = await CreatePreparedMessageImportViewModelAsync(
            activeCase,
            sourceRegistrationService,
            messageImportService,
            new FakeCallImportService(),
            importers: null,
            warningReader: new FakeImportWarningSummaryReader(
            [
                new ImportWarningSummary
                {
                    WarningCode = "missing_platform",
                    Message = "The platform value is missing for one row.",
                    Count = 2
                }
            ]));
        var viewModel = prepared.ViewModel;
        await AdvanceToSummaryStepAsync(viewModel);

        await InvokeAsync(viewModel, "ExecuteImportAsync");

        Assert.Equal("Close", GetStringProperty(viewModel, "NextButtonText"));

        var summaryText = GetStringProperty(viewModel, "SummaryText");
        Assert.Contains("Import completed. The source was registered and the selected records were imported.", summaryText, StringComparison.Ordinal);
        Assert.Contains("Source import ID: source-import-001", summaryText, StringComparison.Ordinal);
        Assert.Contains("Source name: synthetic-message-source", summaryText, StringComparison.Ordinal);
        Assert.Contains("Source type: csv_messages", summaryText, StringComparison.Ordinal);
        Assert.Contains("File hash (SHA-256): abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890", summaryText, StringComparison.Ordinal);
        Assert.Contains("Imported record count: 3", summaryText, StringComparison.Ordinal);
        Assert.Contains("Warning count: 2", summaryText, StringComparison.Ordinal);
        Assert.Contains("Audit event ID: audit-msg-001", summaryText, StringComparison.Ordinal);
        Assert.Contains("Copied file location: imports/source-import-001/original/synthetic-message-source.csv", summaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportWizardViewModel_Successful_Call_Import_Summary_Contains_Counts_Hash_And_Source_Id()
    {
        var activeCase = CreateActiveCase();
        var sourceRegistrationService = new FakeSourceRegistrationService
        {
            Handler = request => Task.FromResult(CreateRegisteredSourceResult(activeCase, request.SourceType, "carrier-test", "synthetic-call-source"))
        };
        var callImportService = new FakeCallImportService
        {
            Handler = request => Task.FromResult(new ImportCallsResult
            {
                CaseId = request.CaseId,
                SourceImportId = request.SourceImportId,
                ImportedCallCount = 4,
                SourceArtifactCount = 4,
                IdentityCountCreated = 2,
                IdentityCountReused = 2,
                WarningCount = 1,
                AuditEventId = "audit-call-001",
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                CompletedAtUtc = DateTimeOffset.UtcNow
            })
        };
        var prepared = await CreatePreparedCallImportViewModelAsync(
            activeCase,
            sourceRegistrationService,
            new FakeMessageImportService(),
            callImportService,
            importers: null,
            warningReader: new FakeImportWarningSummaryReader(
            [
                new ImportWarningSummary
                {
                    WarningCode = "invalid_duration",
                    Message = "The duration value could not be read for one row.",
                    Count = 1
                }
            ]));
        var viewModel = prepared.ViewModel;

        await InvokeAsync(viewModel, "ExecuteImportAsync");

        var summaryText = GetStringProperty(viewModel, "SummaryText");
        Assert.Contains("Import completed. The source was registered and the selected records were imported.", summaryText, StringComparison.Ordinal);
        Assert.Contains("Source import ID: source-import-001", summaryText, StringComparison.Ordinal);
        Assert.Contains("Source name: synthetic-call-source", summaryText, StringComparison.Ordinal);
        Assert.Contains("Source type: csv_calls", summaryText, StringComparison.Ordinal);
        Assert.Contains("Imported record count: 4", summaryText, StringComparison.Ordinal);
        Assert.Contains("Warning count: 1", summaryText, StringComparison.Ordinal);
        Assert.Contains("Audit event ID: audit-call-001", summaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportWizardViewModel_Source_Registration_Failure_Shows_Safe_Error_And_Does_Not_Call_Persistence()
    {
        var activeCase = CreateActiveCase();
        var sourceRegistrationService = new FakeSourceRegistrationService
        {
            Handler = _ => throw new InvalidOperationException("simulated-register-failure")
        };
        var messageImportService = new FakeMessageImportService();
        var callImportService = new FakeCallImportService();
        var prepared = await CreatePreparedMessageImportViewModelAsync(
            activeCase,
            sourceRegistrationService,
            messageImportService,
            callImportService,
            importers: null,
            warningReader: null);
        var viewModel = prepared.ViewModel;
        await AdvanceToSummaryStepAsync(viewModel);

        await InvokeAsync(viewModel, "ExecuteImportAsync");

        Assert.Equal("The source could not be registered safely. Nothing was imported.", GetStringProperty(viewModel, "GeneralErrorMessage"));
        Assert.Equal(1, sourceRegistrationService.CallCount);
        Assert.Equal(0, messageImportService.CallCount);
        Assert.Equal(0, callImportService.CallCount);
    }

    [Fact]
    public async Task ImportWizardViewModel_Persistence_Failure_Shows_Safe_Partial_Failure_Error()
    {
        var activeCase = CreateActiveCase();
        var sourceRegistrationService = new FakeSourceRegistrationService
        {
            Handler = request => Task.FromResult(CreateRegisteredSourceResult(activeCase, request.SourceType, request.Platform, "synthetic-message-source"))
        };
        var messageImportService = new FakeMessageImportService
        {
            Handler = _ => throw new InvalidOperationException("simulated-import-failure")
        };
        var prepared = await CreatePreparedMessageImportViewModelAsync(
            activeCase,
            sourceRegistrationService,
            messageImportService,
            new FakeCallImportService(),
            importers: null,
            warningReader: null);
        var viewModel = prepared.ViewModel;

        await InvokeAsync(viewModel, "ExecuteImportAsync");

        Assert.Equal("The source was registered, but record import did not finish. Review the case and mappings, then try again.", GetStringProperty(viewModel, "GeneralErrorMessage"));

        var summaryText = GetStringProperty(viewModel, "SummaryText");
        Assert.Contains("Import did not complete.", summaryText, StringComparison.Ordinal);
        Assert.Contains("Source import ID: source-import-001", summaryText, StringComparison.Ordinal);
        Assert.Contains("Copied file location: imports/source-import-001/original/synthetic-message-source.csv", summaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportWizardViewModel_Duplicate_Submit_Is_Prevented_While_Running()
    {
        var activeCase = CreateActiveCase();
        var completion = new TaskCompletionSource<ImportMessagesResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceRegistrationService = new FakeSourceRegistrationService
        {
            Handler = request => Task.FromResult(CreateRegisteredSourceResult(activeCase, request.SourceType, request.Platform, "synthetic-message-source"))
        };
        var messageImportService = new FakeMessageImportService
        {
            Handler = async request => await completion.Task.ConfigureAwait(false)
        };
        var prepared = await CreatePreparedMessageImportViewModelAsync(
            activeCase,
            sourceRegistrationService,
            messageImportService,
            new FakeCallImportService(),
            importers: null,
            warningReader: null);
        var viewModel = prepared.ViewModel;

        await AdvanceToSummaryStepAsync(viewModel);

        var nextCommand = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "NextCommand"));
        nextCommand.Execute(null);
        nextCommand.Execute(null);

        await Task.Delay(100);

        Assert.Equal(1, sourceRegistrationService.CallCount);
        Assert.Equal(1, messageImportService.CallCount);

        completion.SetResult(new ImportMessagesResult
        {
            CaseId = activeCase.CaseId,
            SourceImportId = "source-import-001",
            ImportedMessageCount = 2,
            SourceArtifactCount = 2,
            IdentityCountCreated = 1,
            IdentityCountReused = 1,
            RecipientCount = 2,
            WarningCount = 0,
            AuditEventId = "audit-msg-dup",
            StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
            CompletedAtUtc = DateTimeOffset.UtcNow
        });

        await WaitForAsync(() => string.Equals(GetStringProperty(viewModel, "NextButtonText"), "Close", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportWizardViewModel_Logs_Do_Not_Include_Preview_Or_Import_Sensitive_Values()
    {
        const string sensitiveToken = "TOP_SECRET_IMPORT_TOKEN";
        var activeCase = CreateActiveCase();
        var importer = CreateSuccessfulMessageCsvImporter(sensitiveToken);
        var sourceRegistrationService = new FakeSourceRegistrationService
        {
            Handler = request => Task.FromResult(CreateRegisteredSourceResult(activeCase, request.SourceType, "sms", "synthetic-message-source"))
        };
        var messageImportService = new FakeMessageImportService
        {
            Handler = request => Task.FromResult(new ImportMessagesResult
            {
                CaseId = request.CaseId,
                SourceImportId = request.SourceImportId,
                ImportedMessageCount = 1,
                SourceArtifactCount = 1,
                IdentityCountCreated = 1,
                IdentityCountReused = 0,
                RecipientCount = 1,
                WarningCount = 0,
                AuditEventId = "audit-msg-log",
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                CompletedAtUtc = DateTimeOffset.UtcNow
            })
        };
        var prepared = await CreatePreparedMessageImportViewModelAsync(
            activeCase,
            sourceRegistrationService,
            messageImportService,
            new FakeCallImportService(),
            importers: [importer],
            warningReader: null);
        var viewModel = prepared.ViewModel;
        var logs = prepared.Logs;
        SetPropertyValue(viewModel, "FilePath", $@"O:\Synthetic\{sensitiveToken}.csv");

        await InvokeAsync(viewModel, "ProbeFileAsync");
        await InvokeAsync(viewModel, "RefreshPreviewAsync");
        await InvokeAsync(viewModel, "ExecuteImportAsync");

        var flattenedLogs = string.Join(
            Environment.NewLine,
            logs.Select(log =>
            {
                var fields = log.Fields is null
                    ? string.Empty
                    : string.Join(";", log.Fields.Select(pair => $"{pair.Key}={pair.Value}"));
                return $"{log.Operation}|{log.Message}|{fields}";
            }));

        Assert.DoesNotContain(sensitiveToken, flattenedLogs, StringComparison.Ordinal);
    }

    private static async Task AdvanceToSummaryStepAsync(object viewModel)
    {
        while (GetIntProperty(viewModel, "CurrentStepIndex") < 7)
        {
            await InvokeAsync(viewModel, "NextAsync");
        }
    }

    private static async Task<(object ViewModel, List<UiLogEntry> Logs)> CreatePreparedCallImportViewModelAsync(
        CreateCaseResult? activeCase,
        FakeSourceRegistrationService sourceRegistrationService,
        FakeMessageImportService messageImportService,
        FakeCallImportService callImportService,
        IEnumerable<ISourceImporter>? importers = null,
        IImportWarningSummaryReader? warningReader = null)
    {
        List<UiLogEntry> logs;
        var viewModel = CreateImportEnabledViewModel(
            importers ?? [CreateSuccessfulCallCsvImporter()],
            activeCase,
            sourceRegistrationService,
            messageImportService,
            callImportService,
            warningReader ?? new FakeImportWarningSummaryReader(),
            out logs,
            out _);

        SelectSourceType(viewModel, "CSV");
        SetPropertyValue(viewModel, "FilePath", @"O:\Synthetic\calls.csv");
        SetPropertyValue(viewModel, "PlatformText", "carrier-test");
        await InvokeAsync(viewModel, "ProbeFileAsync");
        await InvokeAsync(viewModel, "RefreshPreviewAsync");
        SelectImportKind(viewModel, "Calls");
        return (viewModel, logs);
    }

    private static async Task<(object ViewModel, List<UiLogEntry> Logs)> CreatePreparedMessageImportViewModelAsync(
        CreateCaseResult? activeCase,
        FakeSourceRegistrationService sourceRegistrationService,
        FakeMessageImportService messageImportService,
        FakeCallImportService callImportService,
        IEnumerable<ISourceImporter>? importers = null,
        IImportWarningSummaryReader? warningReader = null)
    {
        List<UiLogEntry> logs;
        var viewModel = CreateImportEnabledViewModel(
            importers ?? [CreateSuccessfulMessageCsvImporter()],
            activeCase,
            sourceRegistrationService,
            messageImportService,
            callImportService,
            warningReader ?? new FakeImportWarningSummaryReader(),
            out logs,
            out _);

        SelectSourceType(viewModel, "CSV");
        SetPropertyValue(viewModel, "FilePath", @"O:\Synthetic\messages.csv");
        SetPropertyValue(viewModel, "PlatformText", "sms");
        await InvokeAsync(viewModel, "ProbeFileAsync");
        await InvokeAsync(viewModel, "RefreshPreviewAsync");
        return (viewModel, logs);
    }

    private static object CreateImportEnabledViewModel(
        IEnumerable<ISourceImporter> importers,
        CreateCaseResult? activeCase,
        ISourceRegistrationService sourceRegistrationService,
        IMessageImportService messageImportService,
        ICallImportService callImportService,
        IImportWarningSummaryReader importWarningSummaryReader,
        out List<UiLogEntry> logs,
        out Counter onCloseCallCount)
    {
        var capturedLogs = new List<UiLogEntry>();
        var capturedCounter = new Counter();
        var assembly = ViewModelAssemblyLoader.Load();
        var viewModelType = assembly.GetType("DumpLens.App.ViewModels.ImportWizardViewModel", throwOnError: true)!;
        Action onClose = () => capturedCounter.Value++;
        Action<string, string, string, IReadOnlyDictionary<string, string>?> logAction =
            (operation, correlationId, message, fields) =>
                capturedLogs.Add(new UiLogEntry(operation, correlationId, message, fields));
        Func<string, IAuditLogger> auditLoggerFactory = _ => new FakeAuditLogger();

        logs = capturedLogs;
        onCloseCallCount = capturedCounter;

        return Activator.CreateInstance(
            viewModelType,
            importers,
            activeCase,
            sourceRegistrationService,
            messageImportService,
            callImportService,
            importWarningSummaryReader,
            auditLoggerFactory,
            onClose,
            logAction,
            "Eastern Standard Time")!;
    }

    private static object CreatePreviewOnlyViewModel(
        IEnumerable<ISourceImporter> importers,
        out List<UiLogEntry> logs,
        out Counter onCloseCallCount)
    {
        var capturedLogs = new List<UiLogEntry>();
        var capturedCounter = new Counter();
        var assembly = ViewModelAssemblyLoader.Load();
        var viewModelType = assembly.GetType("DumpLens.App.ViewModels.ImportWizardViewModel", throwOnError: true)!;
        Action onClose = () => capturedCounter.Value++;
        Action<string, string, string, IReadOnlyDictionary<string, string>?> logAction =
            (operation, correlationId, message, fields) =>
                capturedLogs.Add(new UiLogEntry(operation, correlationId, message, fields));

        logs = capturedLogs;
        onCloseCallCount = capturedCounter;

        return Activator.CreateInstance(
            viewModelType,
            importers,
            onClose,
            logAction,
            "Eastern Standard Time")!;
    }

    private static CreateCaseResult CreateActiveCase()
    {
        return new CreateCaseResult
        {
            CaseId = "case-001",
            PackageId = "package-001",
            CaseNumber = "DL-CASE-001",
            Title = "Synthetic Active Case",
            PackageRootPath = @"O:\Cases\SyntheticActiveCase",
            DatabasePath = @"O:\Cases\SyntheticActiveCase\case.dlensdb",
            ManifestPath = @"O:\Cases\SyntheticActiveCase\manifest.json",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            AuditEventId = "audit-case-001",
            CorrelationId = "corr-case-001"
        };
    }

    private static RegisterSourceResult CreateRegisteredSourceResult(
        CreateCaseResult activeCase,
        string sourceType,
        string? platform,
        string sourceName)
    {
        var storedFilePath = Path.Combine(
            activeCase.PackageRootPath,
            "imports",
            "source-import-001",
            "original",
            $"{sourceName}.csv");

        return new RegisterSourceResult
        {
            SourceImportId = "source-import-001",
            CaseId = activeCase.CaseId,
            SourceName = sourceName,
            SourceType = sourceType,
            Platform = platform,
            OriginalFilename = $"{sourceName}.csv",
            StoredFilePath = storedFilePath,
            SourceFolderPath = Path.Combine(activeCase.PackageRootPath, "imports", "source-import-001"),
            ManifestPath = Path.Combine(activeCase.PackageRootPath, "imports", "source-import-001", "manifest.json"),
            Sha256FilePath = Path.Combine(activeCase.PackageRootPath, "imports", "source-import-001", "sha256.txt"),
            FileSizeBytes = 2048,
            FileSha256 = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
            ImportedAtUtc = DateTimeOffset.UtcNow,
            AuditEventId = "audit-source-001",
            CorrelationId = "corr-source-001"
        };
    }

    private static FakeSourceImporter CreateSuccessfulMessageCsvImporter(string? sensitiveToken = null)
    {
        return new FakeSourceImporter(ImportSourceKind.Csv)
        {
            ProbeResultFactory = request => new ImportProbeResult
            {
                CorrelationId = request.CorrelationId ?? "probe-msg",
                SourceKind = ImportSourceKind.Csv,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                DetectedDelimiter = ',',
                HasHeaderRow = true,
                Columns =
                [
                    new ImportPreviewColumn { Ordinal = 0, SourceColumnName = "timestamp" },
                    new ImportPreviewColumn { Ordinal = 1, SourceColumnName = "sender" },
                    new ImportPreviewColumn { Ordinal = 2, SourceColumnName = "recipient" },
                    new ImportPreviewColumn { Ordinal = 3, SourceColumnName = "message_body" },
                    new ImportPreviewColumn { Ordinal = 4, SourceColumnName = "platform" }
                ],
                PreviewRows =
                [
                    new ImportPreviewRow
                    {
                        RowNumber = 2,
                        Values =
                        [
                            "2026-04-28T10:00:00Z",
                            sensitiveToken ?? "Alpha",
                            "Bravo",
                            sensitiveToken ?? "First preview row",
                            "sms"
                        ]
                    }
                ],
                FieldMappingSuggestions =
                [
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Timestamp, SourceColumnName = "timestamp" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Sender, SourceColumnName = "sender" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Recipient, SourceColumnName = "recipient" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.MessageBody, SourceColumnName = "message_body" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Platform, SourceColumnName = "platform" }
                ]
            },
            PreviewResultFactory = request => new ImportPreviewResult
            {
                CorrelationId = request.CorrelationId ?? "preview-msg",
                SourceKind = ImportSourceKind.Csv,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                DetectedDelimiter = ',',
                HasHeaderRow = true,
                Columns =
                [
                    new ImportPreviewColumn { Ordinal = 0, SourceColumnName = "timestamp" },
                    new ImportPreviewColumn { Ordinal = 1, SourceColumnName = "sender" },
                    new ImportPreviewColumn { Ordinal = 2, SourceColumnName = "recipient" },
                    new ImportPreviewColumn { Ordinal = 3, SourceColumnName = "message_body" },
                    new ImportPreviewColumn { Ordinal = 4, SourceColumnName = "platform" }
                ],
                Rows =
                [
                    new ImportPreviewRow
                    {
                        RowNumber = 2,
                        Values =
                        [
                            "2026-04-28T10:00:00Z",
                            sensitiveToken ?? "Alpha",
                            "Bravo",
                            sensitiveToken ?? "First preview row",
                            "sms"
                        ]
                    }
                ],
                FieldMappingSuggestions =
                [
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Timestamp, SourceColumnName = "timestamp" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Sender, SourceColumnName = "sender" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Recipient, SourceColumnName = "recipient" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.MessageBody, SourceColumnName = "message_body" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Platform, SourceColumnName = "platform" }
                ]
            }
        };
    }

    private static FakeSourceImporter CreateSuccessfulCallCsvImporter()
    {
        return new FakeSourceImporter(ImportSourceKind.Csv)
        {
            ProbeResultFactory = request => new ImportProbeResult
            {
                CorrelationId = request.CorrelationId ?? "probe-call",
                SourceKind = ImportSourceKind.Csv,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                DetectedDelimiter = ',',
                HasHeaderRow = true,
                Columns =
                [
                    new ImportPreviewColumn { Ordinal = 0, SourceColumnName = "timestamp" },
                    new ImportPreviewColumn { Ordinal = 1, SourceColumnName = "caller" },
                    new ImportPreviewColumn { Ordinal = 2, SourceColumnName = "callee" },
                    new ImportPreviewColumn { Ordinal = 3, SourceColumnName = "direction" },
                    new ImportPreviewColumn { Ordinal = 4, SourceColumnName = "duration" }
                ],
                FieldMappingSuggestions =
                [
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Timestamp, SourceColumnName = "timestamp" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Caller, SourceColumnName = "caller" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Callee, SourceColumnName = "callee" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Direction, SourceColumnName = "direction" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Duration, SourceColumnName = "duration" }
                ]
            },
            PreviewResultFactory = request => new ImportPreviewResult
            {
                CorrelationId = request.CorrelationId ?? "preview-call",
                SourceKind = ImportSourceKind.Csv,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                DetectedDelimiter = ',',
                HasHeaderRow = true,
                Columns =
                [
                    new ImportPreviewColumn { Ordinal = 0, SourceColumnName = "timestamp" },
                    new ImportPreviewColumn { Ordinal = 1, SourceColumnName = "caller" },
                    new ImportPreviewColumn { Ordinal = 2, SourceColumnName = "callee" },
                    new ImportPreviewColumn { Ordinal = 3, SourceColumnName = "direction" },
                    new ImportPreviewColumn { Ordinal = 4, SourceColumnName = "duration" }
                ],
                Rows =
                [
                    new ImportPreviewRow
                    {
                        RowNumber = 2,
                        Values =
                        [
                            "2026-04-28T10:00:00Z",
                            "5551112222",
                            "5553334444",
                            "incoming",
                            "60"
                        ]
                    }
                ],
                FieldMappingSuggestions =
                [
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Timestamp, SourceColumnName = "timestamp" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Caller, SourceColumnName = "caller" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Callee, SourceColumnName = "callee" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Direction, SourceColumnName = "direction" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Duration, SourceColumnName = "duration" }
                ]
            }
        };
    }

    private static IEnumerable<object> GetCollection(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return Assert.IsAssignableFrom<IEnumerable>(value).Cast<object>();
    }

    private static int GetIntProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return Assert.IsType<int>(value);
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

    private static string GetImportWizardViewPath()
    {
        return Path.Combine(FindRepositoryRoot(), "src", "DumpLens.App", "ImportWizardView.xaml");
    }

    private static string FindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "DumpLens.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new InvalidOperationException("Could not locate the DumpLens repository root.");
    }

    private static async Task InvokeAsync(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        var task = method!.Invoke(instance, null);
        var awaitedTask = Assert.IsAssignableFrom<Task>(task);
        await awaitedTask;
    }

    private static void SelectImportKind(object viewModel, string label)
    {
        var option = GetCollection(viewModel, "ImportDataKindOptions")
            .Single(sourceType => string.Equals(GetStringProperty(sourceType, "Label"), label, StringComparison.Ordinal));
        SetPropertyValue(viewModel, "SelectedImportDataKindOption", option);
    }

    private static void SelectSourceType(object viewModel, string label)
    {
        var option = GetCollection(viewModel, "SourceTypeOptions")
            .Single(sourceType => string.Equals(GetStringProperty(sourceType, "Label"), label, StringComparison.Ordinal));
        SetPropertyValue(viewModel, "SelectedSourceTypeOption", option);
    }

    private static void SetMapping(object viewModel, string fieldName, string selectedColumnName)
    {
        var mapping = GetCollection(viewModel, "ColumnMappings")
            .Single(candidate => string.Equals(GetStringProperty(candidate, "DumpLensFieldName"), fieldName, StringComparison.Ordinal));
        SetPropertyValue(mapping, "SelectedSourceColumnName", selectedColumnName);
    }

    private static void SetPropertyValue(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var startedAt = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - startedAt > timeoutMs)
            {
                throw new TimeoutException("The expected condition was not reached in time.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public Task<AuditChainVerificationResult> VerifyChainAsync(
            string? caseId,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AuditChainVerificationResult
            {
                IsValid = true,
                CheckedEventCount = 3
            });
        }

        public Task<AuditEventWriteResult> WriteAsync(
            AuditEventDraft draft,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeCallImportService : ICallImportService
    {
        public int CallCount { get; private set; }

        public Func<ImportCallsRequest, Task<ImportCallsResult>>? Handler { get; init; }

        public Task<ImportCallsResult> ImportAsync(
            ImportCallsRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Handler?.Invoke(request) ?? Task.FromResult(new ImportCallsResult
            {
                CaseId = request.CaseId,
                SourceImportId = request.SourceImportId,
                ImportedCallCount = 0,
                SourceArtifactCount = 0,
                IdentityCountCreated = 0,
                IdentityCountReused = 0,
                WarningCount = 0,
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    private sealed class FakeImportWarningSummaryReader : IImportWarningSummaryReader
    {
        private readonly IReadOnlyList<ImportWarningSummary> _summaries;

        public FakeImportWarningSummaryReader(IReadOnlyList<ImportWarningSummary>? summaries = null)
        {
            _summaries = summaries ?? Array.Empty<ImportWarningSummary>();
        }

        public Task<IReadOnlyList<ImportWarningSummary>> GetSummariesAsync(
            string caseDatabasePath,
            string sourceImportId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_summaries);
        }
    }

    private sealed class FakeMessageImportService : IMessageImportService
    {
        public int CallCount { get; private set; }

        public Func<ImportMessagesRequest, Task<ImportMessagesResult>>? Handler { get; init; }

        public Task<ImportMessagesResult> ImportAsync(
            ImportMessagesRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Handler?.Invoke(request) ?? Task.FromResult(new ImportMessagesResult
            {
                CaseId = request.CaseId,
                SourceImportId = request.SourceImportId,
                ImportedMessageCount = 0,
                SourceArtifactCount = 0,
                IdentityCountCreated = 0,
                IdentityCountReused = 0,
                RecipientCount = 0,
                WarningCount = 0,
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    private sealed class FakeSourceImporter : ISourceImporter
    {
        public FakeSourceImporter(ImportSourceKind sourceKind)
        {
            SourceKind = sourceKind;
        }

        public Func<ImportPreviewRequest, ImportPreviewResult>? PreviewResultFactory { get; init; }

        public Func<ImportTabularDataRequest, ImportTabularDataResult>? ReadResultFactory { get; init; }

        public Func<ImportProbeRequest, ImportProbeResult>? ProbeResultFactory { get; init; }

        public ImportSourceKind SourceKind { get; }

        public bool CanHandle(string filePath)
        {
            return true;
        }

        public Task<ImportPreviewResult> PreviewAsync(ImportPreviewRequest request, CancellationToken cancellationToken = default)
        {
            var result = PreviewResultFactory?.Invoke(request) ?? new ImportPreviewResult
            {
                CorrelationId = request.CorrelationId ?? "preview-default",
                SourceKind = SourceKind,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true
            };

            return Task.FromResult(result);
        }

        public Task<ImportTabularDataResult> ReadTabularDataAsync(ImportTabularDataRequest request, CancellationToken cancellationToken = default)
        {
            var result = ReadResultFactory?.Invoke(request) ?? new ImportTabularDataResult
            {
                CorrelationId = request.CorrelationId ?? "read-default",
                SourceKind = SourceKind,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true
            };

            return Task.FromResult(result);
        }

        public Task<ImportProbeResult> ProbeAsync(ImportProbeRequest request, CancellationToken cancellationToken = default)
        {
            var result = ProbeResultFactory?.Invoke(request) ?? new ImportProbeResult
            {
                CorrelationId = request.CorrelationId ?? "probe-default",
                SourceKind = SourceKind,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true
            };

            return Task.FromResult(result);
        }
    }

    private sealed class FakeSourceRegistrationService : ISourceRegistrationService
    {
        public int CallCount { get; private set; }

        public Func<RegisterSourceRequest, Task<RegisterSourceResult>>? Handler { get; init; }

        public Task<RegisterSourceResult> RegisterAsync(
            RegisterSourceRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Handler?.Invoke(request) ?? Task.FromResult(new RegisterSourceResult
            {
                SourceImportId = "source-import-001",
                CaseId = request.CaseId,
                SourceName = "synthetic-source",
                SourceType = request.SourceType,
                Platform = request.Platform,
                OriginalFilename = "synthetic-source.csv",
                StoredFilePath = Path.Combine(request.CasePackageRootPath, "imports", "source-import-001", "original", "synthetic-source.csv"),
                SourceFolderPath = Path.Combine(request.CasePackageRootPath, "imports", "source-import-001"),
                ManifestPath = Path.Combine(request.CasePackageRootPath, "imports", "source-import-001", "manifest.json"),
                Sha256FilePath = Path.Combine(request.CasePackageRootPath, "imports", "source-import-001", "sha256.txt"),
                FileSizeBytes = 1024,
                FileSha256 = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
                ImportedAtUtc = DateTimeOffset.UtcNow,
                CorrelationId = request.CorrelationId ?? "corr-source-default"
            });
        }
    }

    private sealed record UiLogEntry(
        string Operation,
        string CorrelationId,
        string Message,
        IReadOnlyDictionary<string, string>? Fields);

    private sealed class Counter
    {
        public int Value { get; set; }
    }
}
