using System.Collections.ObjectModel;
using System.Globalization;
using DumpLens.Application.Audit;
using DumpLens.Application.CallImports;
using DumpLens.Application.Cases;
using DumpLens.Application.Imports;
using DumpLens.Application.MessageImports;
using DumpLens.Application.Sources;
using System.Windows.Input;

namespace DumpLens.App.ViewModels;

public sealed class ImportWizardViewModel : ObservableObject
{
    private const string WizardOpenedOperation = "import_wizard_opened";
    private const string FileSelectedOperation = "import_wizard_file_selected";
    private const string PreviewRequestedOperation = "import_wizard_preview_requested";
    private const string PreviewSucceededOperation = "import_wizard_preview_succeeded";
    private const string PreviewFailedOperation = "import_wizard_preview_failed";
    private const string WizardClosedOperation = "import_wizard_closed";
    private const string ImportCompletionRequestedOperation = "import_wizard_completion_requested";
    private const string ActiveCaseValidationFailedOperation = "import_wizard_active_case_validation_failed";
    private const string ImportKindSelectedOperation = "import_wizard_kind_selected";
    private const string SourceRegistrationRequestedOperation = "import_wizard_source_registration_requested";
    private const string SourceRegistrationSucceededOperation = "import_wizard_source_registration_succeeded";
    private const string SourceRegistrationFailedOperation = "import_wizard_source_registration_failed";
    private const string MessagePersistenceRequestedOperation = "import_wizard_message_persistence_requested";
    private const string CallPersistenceRequestedOperation = "import_wizard_call_persistence_requested";
    private const string PersistenceSucceededOperation = "import_wizard_persistence_succeeded";
    private const string PersistenceFailedOperation = "import_wizard_persistence_failed";
    private const string ImportWorkflowCompletedOperation = "import_wizard_workflow_completed";
    private const string ImportWorkflowFailedOperation = "import_wizard_workflow_failed";
    private const int PreviewRowCount = 10;

    private static readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> NoOpLogAction = static (_, _, _, _) => { };

    private readonly CreateCaseResult? _activeCase;
    private readonly Func<string, IAuditLogger>? _auditLoggerFactory;
    private readonly ICallImportService _callImportService;
    private readonly IReadOnlyDictionary<ImportSourceKind, ISourceImporter> _importers;
    private readonly IImportWarningSummaryReader _importWarningSummaryReader;
    private readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> _logAction;
    private readonly IMessageImportService _messageImportService;
    private readonly AsyncRelayCommand _nextCommand;
    private readonly Action _onClose;
    private readonly AsyncRelayCommand _probeFileCommand;
    private readonly AsyncRelayCommand _refreshPreviewCommand;
    private readonly ISourceRegistrationService _sourceRegistrationService;
    private readonly RelayCommand _backCommand;
    private readonly RelayCommand _browsePlaceholderCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly string _correlationId;
    private IReadOnlyList<ImportPreviewColumn> _latestColumns;
    private IReadOnlyList<ImportFieldMappingSuggestion> _latestFieldMappingSuggestions;
    private IReadOnlyList<ImportWarningSummary> _latestImportedWarningSummaries;
    private IReadOnlyList<ImportWarning> _previewWarnings;
    private IReadOnlyList<ImportWarning> _probeWarnings;
    private ImportSourceTypeOptionViewModel? _selectedSourceTypeOption;
    private ImportDataKindOptionViewModel? _selectedImportDataKindOption;
    private ImportWarningViewModel? _selectedWarning;
    private RegisterSourceResult? _registeredSourceResult;
    private bool _hasExecutedImport;
    private bool _hasImportResult;
    private bool _hasManualImportDataKindSelection;
    private bool _hasPreviewResult;
    private bool _hasProbeResult;
    private bool _importCompleted;
    private bool _isBusy;
    private bool _isPreviewCurrent;
    private bool _isProbeSupported;
    private bool _summaryCloseOnly;
    private int _currentStepIndex;
    private int _importedRecordCount;
    private int _persistedWarningCount;
    private string _detectedSourceKindText;
    private string _filePath;
    private string _fileSupportStatusText;
    private string _generalErrorMessage;
    private string _platformText;
    private string _probeDetailsText;
    private string _sourceAccountText;
    private string _sourceDeviceText;
    private string _sourceOwnerText;
    private string _statusMessage;
    private string _timezoneText;
    private string? _importAuditEventId;
    private string? _selectedWorksheetName;

    public ImportWizardViewModel(
        IEnumerable<ISourceImporter> sourceImporters,
        Action onClose,
        Action<string, string, string, IReadOnlyDictionary<string, string>?>? logAction = null,
        string? defaultTimezone = null)
        : this(
            sourceImporters,
            activeCase: null,
            new UnavailableSourceRegistrationService(),
            new UnavailableMessageImportService(),
            new UnavailableCallImportService(),
            new EmptyImportWarningSummaryReader(),
            auditLoggerFactory: null,
            onClose,
            logAction,
            defaultTimezone)
    {
    }

    public ImportWizardViewModel(
        IEnumerable<ISourceImporter> sourceImporters,
        CreateCaseResult? activeCase,
        ISourceRegistrationService sourceRegistrationService,
        IMessageImportService messageImportService,
        ICallImportService callImportService,
        IImportWarningSummaryReader importWarningSummaryReader,
        Func<string, IAuditLogger>? auditLoggerFactory,
        Action onClose,
        Action<string, string, string, IReadOnlyDictionary<string, string>?>? logAction = null,
        string? defaultTimezone = null)
    {
        ArgumentNullException.ThrowIfNull(sourceImporters);

        _activeCase = activeCase;
        _sourceRegistrationService = sourceRegistrationService ?? throw new ArgumentNullException(nameof(sourceRegistrationService));
        _messageImportService = messageImportService ?? throw new ArgumentNullException(nameof(messageImportService));
        _callImportService = callImportService ?? throw new ArgumentNullException(nameof(callImportService));
        _importWarningSummaryReader = importWarningSummaryReader ?? throw new ArgumentNullException(nameof(importWarningSummaryReader));
        _auditLoggerFactory = auditLoggerFactory;
        _onClose = onClose ?? throw new ArgumentNullException(nameof(onClose));
        _logAction = logAction ?? NoOpLogAction;
        _correlationId = Guid.NewGuid().ToString("N");
        _importers = sourceImporters
            .GroupBy(static importer => importer.SourceKind)
            .ToDictionary(static group => group.Key, static group => group.First());
        _latestColumns = Array.Empty<ImportPreviewColumn>();
        _latestFieldMappingSuggestions = Array.Empty<ImportFieldMappingSuggestion>();
        _latestImportedWarningSummaries = Array.Empty<ImportWarningSummary>();
        _probeWarnings = Array.Empty<ImportWarning>();
        _previewWarnings = Array.Empty<ImportWarning>();
        _detectedSourceKindText = "No file inspected yet.";
        _filePath = string.Empty;
        _fileSupportStatusText = "Select a source type and inspect a file to continue.";
        _generalErrorMessage = string.Empty;
        _platformText = string.Empty;
        _probeDetailsText = "Preview and probe are available before import. The final step registers, copies, hashes, and persists the source only after you confirm.";
        _sourceAccountText = string.Empty;
        _sourceDeviceText = string.Empty;
        _sourceOwnerText = string.Empty;
        _statusMessage = activeCase is null
            ? "Preview is available, but create or open a case before importing sources."
            : $"Active case ready: {BuildCaseLabel(activeCase)}";
        _timezoneText = string.IsNullOrWhiteSpace(defaultTimezone) ? TimeZoneInfo.Local.Id : defaultTimezone.Trim();

        Steps = new ObservableCollection<ImportWizardStepViewModel>(CreateSteps());
        SourceTypeOptions = new ObservableCollection<ImportSourceTypeOptionViewModel>(CreateSourceTypeOptions());
        ImportDataKindOptions = new ObservableCollection<ImportDataKindOptionViewModel>(CreateImportDataKindOptions());
        WorksheetOptions = new ObservableCollection<string>();
        ColumnMappings = new ObservableCollection<ImportColumnMappingViewModel>();
        Warnings = new ObservableCollection<ImportWarningViewModel>();
        PreviewGrid = new ImportPreviewGridViewModel();

        _nextCommand = new AsyncRelayCommand(NextAsync, () => !IsBusy);
        _backCommand = new RelayCommand(Back, () => !IsBusy && CurrentStepIndex > 0);
        _cancelCommand = new RelayCommand(Cancel, () => !IsBusy);
        _probeFileCommand = new AsyncRelayCommand(ProbeFileAsync, () => !IsBusy);
        _refreshPreviewCommand = new AsyncRelayCommand(RefreshPreviewAsync, () => !IsBusy);
        _browsePlaceholderCommand = new RelayCommand(ShowBrowsePlaceholder, () => !IsBusy);

        ApplyImportDataKindSelection(ImportDataKind.Messages, markManual: false, logSelection: false);
        UpdateStepState();

        _logAction(
            WizardOpenedOperation,
            _correlationId,
            "Import wizard opened.",
            CreateBaseLogFields());
    }

    public RelayCommand BackCommand => _backCommand;

    public RelayCommand BrowsePlaceholderCommand => _browsePlaceholderCommand;

    public RelayCommand CancelCommand => _cancelCommand;

    public ObservableCollection<ImportColumnMappingViewModel> ColumnMappings { get; }

    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        private set
        {
            if (!SetProperty(ref _currentStepIndex, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CurrentStep));
            OnPropertyChanged(nameof(CurrentStepTitle));
            OnPropertyChanged(nameof(CurrentStepDescription));
            OnPropertyChanged(nameof(IsChooseSourceStep));
            OnPropertyChanged(nameof(IsSelectFileStep));
            OnPropertyChanged(nameof(IsAssignSourceStep));
            OnPropertyChanged(nameof(IsPreviewStep));
            OnPropertyChanged(nameof(IsMappingStep));
            OnPropertyChanged(nameof(IsTimezoneStep));
            OnPropertyChanged(nameof(IsWarningsStep));
            OnPropertyChanged(nameof(IsSummaryStep));
            OnPropertyChanged(nameof(NextButtonText));
            UpdateStepState();
            RaiseCommandStateChanged();
        }
    }

    public ImportWizardStepViewModel CurrentStep => Steps[CurrentStepIndex];

    public string CurrentStepDescription => CurrentStep.Description;

    public string CurrentStepTitle => CurrentStep.Title;

    public string DetectedSourceKindText
    {
        get => _detectedSourceKindText;
        private set => SetProperty(ref _detectedSourceKindText, value);
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            var normalized = value ?? string.Empty;
            if (!SetProperty(ref _filePath, normalized))
            {
                return;
            }

            GeneralErrorMessage = string.Empty;
            StatusMessage = "Inspect the file to load support details and worksheet options.";
            InvalidateProbeAndPreview(resetWorksheetSelection: true);
        }
    }

    public string FileSupportStatusText
    {
        get => _fileSupportStatusText;
        private set => SetProperty(ref _fileSupportStatusText, value);
    }

    public string GeneralErrorMessage
    {
        get => _generalErrorMessage;
        private set
        {
            if (!SetProperty(ref _generalErrorMessage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasGeneralErrorMessage));
        }
    }

    public bool HasGeneralErrorMessage => !string.IsNullOrWhiteSpace(GeneralErrorMessage);

    public bool HasPreviewResult => _hasPreviewResult;

    public bool HasProbeResult => _hasProbeResult;

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasActiveCase => _activeCase is not null;

    public ObservableCollection<ImportDataKindOptionViewModel> ImportDataKindOptions { get; }

    public bool IsAssignSourceStep => CurrentStepIndex == 2;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(NextButtonText));
            RaiseCommandStateChanged();
        }
    }

    public bool IsChooseSourceStep => CurrentStepIndex == 0;

    public bool IsMappingStep => CurrentStepIndex == 4;

    public bool IsPreviewCurrent => _isPreviewCurrent;

    public bool IsPreviewStep => CurrentStepIndex == 3;

    public bool IsProbeSupported => _isProbeSupported;

    public bool IsSelectFileStep => CurrentStepIndex == 1;

    public bool IsSummaryStep => CurrentStepIndex == 7;

    public bool IsTimezoneStep => CurrentStepIndex == 5;

    public bool IsWarningsStep => CurrentStepIndex == 6;

    public string NextButtonText
    {
        get
        {
            if (!IsSummaryStep)
            {
                return "Next";
            }

            if (_summaryCloseOnly)
            {
                return "Close";
            }

            return IsBusy ? "Importing..." : "Import";
        }
    }

    public AsyncRelayCommand NextCommand => _nextCommand;

    public string PlatformText
    {
        get => _platformText;
        set
        {
            if (!SetProperty(ref _platformText, value ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public ImportPreviewGridViewModel PreviewGrid { get; }

    public string ProbeDetailsText
    {
        get => _probeDetailsText;
        private set => SetProperty(ref _probeDetailsText, value);
    }

    public AsyncRelayCommand ProbeFileCommand => _probeFileCommand;

    public AsyncRelayCommand RefreshPreviewCommand => _refreshPreviewCommand;

    public ImportDataKindOptionViewModel? SelectedImportDataKindOption
    {
        get => _selectedImportDataKindOption;
        set
        {
            if (value is null)
            {
                return;
            }

            if (!SetProperty(ref _selectedImportDataKindOption, value))
            {
                return;
            }

            _hasManualImportDataKindSelection = true;
            OnPropertyChanged(nameof(SummaryText));
            _logAction(
                ImportKindSelectedOperation,
                _correlationId,
                "Import kind selected.",
                CreateImportKindFields(value.DataKind));
        }
    }

    public ImportSourceTypeOptionViewModel? SelectedSourceTypeOption
    {
        get => _selectedSourceTypeOption;
        set
        {
            if (!SetProperty(ref _selectedSourceTypeOption, value))
            {
                return;
            }

            GeneralErrorMessage = string.Empty;
            StatusMessage = value is null
                ? "Choose a source type to begin."
                : $"Selected {value.Label}. Inspect a file to continue.";
            InvalidateProbeAndPreview(resetWorksheetSelection: true);
        }
    }

    public ImportWarningViewModel? SelectedWarning
    {
        get => _selectedWarning;
        set => SetProperty(ref _selectedWarning, value);
    }

    public string? SelectedWorksheetName
    {
        get => _selectedWorksheetName;
        set
        {
            if (!SetProperty(ref _selectedWorksheetName, value))
            {
                return;
            }

            if (_hasPreviewResult)
            {
                _isPreviewCurrent = false;
                OnPropertyChanged(nameof(IsPreviewCurrent));
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                StatusMessage = "Worksheet selection updated. Refresh preview to review the selected worksheet.";
            }

            if (!_hasManualImportDataKindSelection)
            {
                ApplyImportDataKindSelection(InferImportDataKind(), markManual: false, logSelection: false);
            }

            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string SourceAccountText
    {
        get => _sourceAccountText;
        set => SetProperty(ref _sourceAccountText, value ?? string.Empty);
    }

    public ObservableCollection<ImportSourceTypeOptionViewModel> SourceTypeOptions { get; }

    public string SourceDeviceText
    {
        get => _sourceDeviceText;
        set => SetProperty(ref _sourceDeviceText, value ?? string.Empty);
    }

    public string SourceOwnerText
    {
        get => _sourceOwnerText;
        set => SetProperty(ref _sourceOwnerText, value ?? string.Empty);
    }

    public ObservableCollection<ImportWizardStepViewModel> Steps { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SummaryText
    {
        get
        {
            if (!_hasImportResult)
            {
                return BuildPendingSummaryText();
            }

            return _importCompleted
                ? BuildCompletedSummaryText()
                : BuildIncompleteSummaryText();
        }
    }

    public string TimezoneText
    {
        get => _timezoneText;
        set
        {
            if (!SetProperty(ref _timezoneText, value ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string WarningSummaryText =>
        Warnings.Count == 0
            ? "No import warnings are currently loaded."
            : $"{Warnings.Count} warning(s) are available for review.";

    public ObservableCollection<ImportWarningViewModel> Warnings { get; }

    public ObservableCollection<string> WorksheetOptions { get; }

    public async Task ExecuteImportAsync()
    {
        GeneralErrorMessage = string.Empty;

        _logAction(
            ImportCompletionRequestedOperation,
            _correlationId,
            "Import completion requested.",
            CreateImportExecutionFields());

        if (!ValidateSourceTypeSelection())
        {
            LogWorkflowFailed("validation", "source_type_missing");
            return;
        }

        if (!TryNormalizeAbsoluteFilePath(FilePath, out var fullPath, out var validationMessage))
        {
            GeneralErrorMessage = validationMessage;
            StatusMessage = "Select and inspect a supported CSV or XLSX source before importing.";
            LogWorkflowFailed("validation", "file_path_invalid");
            return;
        }

        if (_activeCase is null)
        {
            GeneralErrorMessage = "Create or open a case before importing sources.";
            StatusMessage = "Create or open a case before importing sources.";
            _logAction(
                ActiveCaseValidationFailedOperation,
                _correlationId,
                "Import blocked because no active case is available.",
                CreateImportExecutionFields());
            LogWorkflowFailed("validation", "active_case_missing");
            return;
        }

        if (!HasProbeResult || !IsProbeSupported)
        {
            GeneralErrorMessage = "Inspect a supported CSV or XLSX source before importing.";
            StatusMessage = "Import is blocked until the selected source has been inspected successfully.";
            LogWorkflowFailed("validation", "probe_missing_or_unsupported");
            return;
        }

        if (!HasPreviewResult)
        {
            GeneralErrorMessage = "Load preview data before importing.";
            StatusMessage = "Refresh preview before importing so the current mappings and warnings can be reviewed.";
            LogWorkflowFailed("validation", "preview_missing");
            return;
        }

        if (!IsPreviewCurrent)
        {
            GeneralErrorMessage = "Refresh preview after changing the worksheet before importing.";
            StatusMessage = "Preview is out of date. Refresh preview before importing.";
            LogWorkflowFailed("validation", "preview_not_current");
            return;
        }

        if (!ValidateRequiredMappings(out var mappingValidationMessage))
        {
            GeneralErrorMessage = mappingValidationMessage;
            StatusMessage = "Review the required mappings before importing.";
            LogWorkflowFailed("validation", "required_mappings_missing");
            return;
        }

        var selectedKind = SelectedImportDataKindOption?.DataKind ?? ImportDataKind.Messages;
        var registeredSource = default(RegisterSourceResult);
        var importedRecordCount = 0;
        var warningCount = 0;
        var auditEventId = default(string);

        IsBusy = true;
        try
        {
            StatusMessage = "Registering and copying the selected source...";

            _logAction(
                SourceRegistrationRequestedOperation,
                _correlationId,
                "Source registration requested.",
                CreateImportExecutionFields());

            try
            {
                registeredSource = await _sourceRegistrationService.RegisterAsync(
                    BuildRegisterSourceRequest(fullPath, selectedKind),
                    CancellationToken.None).ConfigureAwait(false);
                _registeredSourceResult = registeredSource;

                _logAction(
                    SourceRegistrationSucceededOperation,
                    _correlationId,
                    "Source registration succeeded.",
                    CreateSourceRegistrationSucceededFields(registeredSource, selectedKind));
            }
            catch (Exception exception)
            {
                GeneralErrorMessage = "The source could not be registered safely. Nothing was imported.";
                StatusMessage = "Review the selected file and active case, then try again.";
                _logAction(
                    SourceRegistrationFailedOperation,
                    _correlationId,
                    "Source registration failed.",
                    CreateFailureFields(fullPath, exception.GetType().Name));
                LogWorkflowFailed("source_registration", exception.GetType().Name);
                _hasImportResult = false;
                _summaryCloseOnly = false;
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(NextButtonText));
                return;
            }

            try
            {
                switch (selectedKind)
                {
                    case ImportDataKind.Messages:
                    {
                        StatusMessage = "Persisting mapped message records...";
                        _logAction(
                            MessagePersistenceRequestedOperation,
                            _correlationId,
                            "Message persistence requested.",
                            CreatePersistenceRequestedFields(registeredSource, selectedKind));

                        var result = await _messageImportService.ImportAsync(
                            BuildImportMessagesRequest(registeredSource),
                            CancellationToken.None).ConfigureAwait(false);
                        importedRecordCount = result.ImportedMessageCount;
                        warningCount = result.WarningCount;
                        auditEventId = result.AuditEventId;
                        break;
                    }
                    case ImportDataKind.Calls:
                    {
                        StatusMessage = "Persisting mapped call records...";
                        _logAction(
                            CallPersistenceRequestedOperation,
                            _correlationId,
                            "Call persistence requested.",
                            CreatePersistenceRequestedFields(registeredSource, selectedKind));

                        var result = await _callImportService.ImportAsync(
                            BuildImportCallsRequest(registeredSource),
                            CancellationToken.None).ConfigureAwait(false);
                        importedRecordCount = result.ImportedCallCount;
                        warningCount = result.WarningCount;
                        auditEventId = result.AuditEventId;
                        break;
                    }
                    default:
                        throw new InvalidOperationException("The selected import kind is not supported.");
                }

                _logAction(
                    PersistenceSucceededOperation,
                    _correlationId,
                    "Persistence succeeded.",
                    CreatePersistenceSucceededFields(registeredSource, selectedKind, importedRecordCount, warningCount, auditEventId));
            }
            catch (Exception exception)
            {
                await ApplyPartialFailureAsync(
                    fullPath,
                    exception.GetType().Name,
                    registeredSource,
                    selectedKind,
                    "The source was registered, but record import did not finish. Review the case and mappings, then try again.")
                    .ConfigureAwait(false);
                return;
            }

            _latestImportedWarningSummaries = await LoadImportedWarningSummariesAsync(registeredSource).ConfigureAwait(false);

            if (!await VerifyAuditChainAsync(_activeCase.CaseId).ConfigureAwait(false))
            {
                _importedRecordCount = importedRecordCount;
                _persistedWarningCount = warningCount;
                _importAuditEventId = auditEventId;
                _hasImportResult = true;
                _hasExecutedImport = true;
                _importCompleted = false;
                _summaryCloseOnly = true;
                GeneralErrorMessage = "The source was registered and records were imported, but audit verification did not complete safely.";
                StatusMessage = "Close this wizard and review the case logs before relying on the import.";
                LogWorkflowFailed("audit_verification", "audit_chain_invalid");
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(NextButtonText));
                return;
            }

            _importedRecordCount = importedRecordCount;
            _persistedWarningCount = warningCount;
            _importAuditEventId = auditEventId;
            _hasImportResult = true;
            _hasExecutedImport = true;
            _importCompleted = true;
            _summaryCloseOnly = true;
            GeneralErrorMessage = string.Empty;
            StatusMessage = "Import completed. The source is registered and the selected records were imported.";

            _logAction(
                ImportWorkflowCompletedOperation,
                _correlationId,
                "Import workflow completed.",
                CreatePersistenceSucceededFields(registeredSource, selectedKind, importedRecordCount, warningCount, auditEventId));

            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(NextButtonText));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task NextAsync()
    {
        GeneralErrorMessage = string.Empty;

        switch (CurrentStepIndex)
        {
            case 0:
                if (!ValidateSourceTypeSelection())
                {
                    return;
                }

                CurrentStepIndex = 1;
                StatusMessage = "Enter an absolute file path and inspect the file before continuing.";
                return;

            case 1:
                if (!HasProbeResult || !IsProbeSupported)
                {
                    GeneralErrorMessage = "Inspect a supported CSV or XLSX file before continuing.";
                    return;
                }

                CurrentStepIndex = 2;
                StatusMessage = "Confirm import kind, platform, and optional source placeholder labels.";
                return;

            case 2:
                CurrentStepIndex = 3;
                if (!_hasPreviewResult || !_isPreviewCurrent)
                {
                    await RefreshPreviewAsync().ConfigureAwait(false);
                }

                return;

            case 3:
                if (!HasPreviewResult)
                {
                    GeneralErrorMessage = "Load preview data before continuing.";
                    return;
                }

                CurrentStepIndex = 4;
                StatusMessage = "Review and adjust the suggested column mappings.";
                return;

            case 4:
                CurrentStepIndex = 5;
                StatusMessage = "Confirm the timestamp and timezone context for import.";
                return;

            case 5:
                CurrentStepIndex = 6;
                StatusMessage = "Review warnings and extraction limitations before importing.";
                return;

            case 6:
                CurrentStepIndex = 7;
                StatusMessage = "Review the import summary and confirm when ready.";
                OnPropertyChanged(nameof(SummaryText));
                return;

            case 7:
                if (_summaryCloseOnly)
                {
                    CloseWizard(_importCompleted ? "import_completed" : "import_closed_after_failure");
                    return;
                }

                await ExecuteImportAsync().ConfigureAwait(false);
                return;
        }
    }

    public async Task ProbeFileAsync()
    {
        GeneralErrorMessage = string.Empty;

        if (!ValidateSourceTypeSelection())
        {
            return;
        }

        if (!TryNormalizeAbsoluteFilePath(FilePath, out var fullPath, out var validationMessage))
        {
            GeneralErrorMessage = validationMessage;
            return;
        }

        var importer = ResolveSelectedImporter();
        if (importer is null)
        {
            GeneralErrorMessage = "The selected source type is not configured in this shell.";
            return;
        }

        InvalidateProbeAndPreview(resetWorksheetSelection: true);
        IsBusy = true;
        StatusMessage = "Inspecting file support and worksheet details...";

        _logAction(
            FileSelectedOperation,
            _correlationId,
            "Import file selected for inspection.",
            CreateFileOperationFields(fullPath));

        try
        {
            var probeResult = await importer.ProbeAsync(new ImportProbeRequest
            {
                FilePath = fullPath,
                PreviewRowCount = PreviewRowCount,
                CorrelationId = _correlationId
            }).ConfigureAwait(false);

            ApplyProbeResult(probeResult);

            if (probeResult.IsSupported)
            {
                StatusMessage = "File inspection complete. Continue to import setup or preview data.";
            }
            else
            {
                GeneralErrorMessage = BuildUnsupportedFileMessage();
                StatusMessage = "File inspection completed with warnings. Review the support status before trying again.";
            }
        }
        catch (Exception exception)
        {
            ApplySafePreviewFailure("The file could not be inspected safely. Check the file path and try again.");
            _logAction(
                PreviewFailedOperation,
                _correlationId,
                "Import preview failed during file inspection.",
                CreateFailureFields(fullPath, exception.GetType().Name));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshPreviewAsync()
    {
        GeneralErrorMessage = string.Empty;

        if (!ValidateSourceTypeSelection())
        {
            return;
        }

        if (!TryNormalizeAbsoluteFilePath(FilePath, out var fullPath, out var validationMessage))
        {
            GeneralErrorMessage = validationMessage;
            return;
        }

        var importer = ResolveSelectedImporter();
        if (importer is null)
        {
            GeneralErrorMessage = "The selected source type is not configured in this shell.";
            return;
        }

        if (!HasProbeResult)
        {
            await ProbeFileAsync().ConfigureAwait(false);
            if (!HasProbeResult)
            {
                return;
            }
        }

        IsBusy = true;
        StatusMessage = "Loading preview rows and mapping suggestions...";

        _logAction(
            PreviewRequestedOperation,
            _correlationId,
            "Import preview requested.",
            CreatePreviewRequestFields(fullPath));

        try
        {
            var previewResult = await importer.PreviewAsync(new ImportPreviewRequest
            {
                FilePath = fullPath,
                WorksheetName = SelectedWorksheetName,
                RowCount = PreviewRowCount,
                CorrelationId = _correlationId
            }).ConfigureAwait(false);

            if (!previewResult.IsSupported)
            {
                _previewWarnings = previewResult.Warnings;
                RefreshWarnings();
                GeneralErrorMessage = BuildUnsupportedFileMessage();
                StatusMessage = "Preview could not be loaded for the selected source. Review the warnings and try another file.";
                _hasPreviewResult = false;
                _isPreviewCurrent = false;
                OnPropertyChanged(nameof(HasPreviewResult));
                OnPropertyChanged(nameof(IsPreviewCurrent));
                PreviewGrid.Clear();
                ColumnMappings.Clear();
                _logAction(
                    PreviewFailedOperation,
                    _correlationId,
                    "Import preview failed because the file is unsupported.",
                    CreateFailureFields(fullPath, "unsupported_file"));
                return;
            }

            ApplyPreviewResult(previewResult);

            StatusMessage = previewResult.ReturnedRowCount == 0
                ? "Preview loaded, but no preview rows are available for the selected source."
                : "Preview loaded. Review rows, mappings, timezone, warnings, and import kind before importing.";

            _logAction(
                PreviewSucceededOperation,
                _correlationId,
                "Import preview succeeded.",
                CreatePreviewSuccessFields(fullPath, previewResult));
        }
        catch (Exception exception)
        {
            ApplySafePreviewFailure("Preview could not be loaded safely. Check the source selection and try again.");
            _logAction(
                PreviewFailedOperation,
                _correlationId,
                "Import preview failed.",
                CreateFailureFields(fullPath, exception.GetType().Name));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyPartialFailureAsync(
        string fullPath,
        string failureType,
        RegisterSourceResult registeredSource,
        ImportDataKind selectedKind,
        string safeUserMessage)
    {
        _latestImportedWarningSummaries = await LoadImportedWarningSummariesAsync(registeredSource).ConfigureAwait(false);
        _importedRecordCount = 0;
        _persistedWarningCount = _latestImportedWarningSummaries.Sum(static summary => summary.Count);
        _importAuditEventId = registeredSource.AuditEventId;
        _hasImportResult = true;
        _hasExecutedImport = true;
        _importCompleted = false;
        _summaryCloseOnly = true;
        GeneralErrorMessage = safeUserMessage;
        StatusMessage = "The source copy and registration were preserved. Close this wizard and review the case before retrying.";

        _logAction(
            PersistenceFailedOperation,
            _correlationId,
            "Persistence failed after source registration.",
            CreatePersistenceFailedFields(registeredSource, selectedKind, failureType));
        LogWorkflowFailed("persistence", failureType);
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(NextButtonText));
    }

    private void ApplyImportDataKindSelection(ImportDataKind dataKind, bool markManual, bool logSelection)
    {
        var matchingOption = ImportDataKindOptions.First(option => option.DataKind == dataKind);
        if (ReferenceEquals(_selectedImportDataKindOption, matchingOption))
        {
            if (markManual)
            {
                _hasManualImportDataKindSelection = true;
            }

            return;
        }

        _selectedImportDataKindOption = matchingOption;
        OnPropertyChanged(nameof(SelectedImportDataKindOption));
        OnPropertyChanged(nameof(SummaryText));

        if (markManual)
        {
            _hasManualImportDataKindSelection = true;
        }

        if (logSelection)
        {
            _logAction(
                ImportKindSelectedOperation,
                _correlationId,
                "Import kind selected.",
                CreateImportKindFields(dataKind));
        }
    }

    private void ApplyPreviewResult(ImportPreviewResult previewResult)
    {
        _previewWarnings = previewResult.Warnings;
        _latestColumns = previewResult.Columns;
        _latestFieldMappingSuggestions = previewResult.FieldMappingSuggestions;
        _hasPreviewResult = true;
        _isPreviewCurrent = true;
        OnPropertyChanged(nameof(HasPreviewResult));
        OnPropertyChanged(nameof(IsPreviewCurrent));

        PreviewGrid.Load(previewResult.Columns, previewResult.Rows);
        RefreshMappings();
        RefreshWarnings();

        if (SelectedWorksheetName is null && !string.IsNullOrWhiteSpace(previewResult.SelectedWorksheetName))
        {
            _selectedWorksheetName = previewResult.SelectedWorksheetName;
            OnPropertyChanged(nameof(SelectedWorksheetName));
        }

        if (!_hasManualImportDataKindSelection)
        {
            ApplyImportDataKindSelection(InferImportDataKind(), markManual: false, logSelection: false);
        }

        OnPropertyChanged(nameof(SummaryText));
    }

    private void ApplyProbeResult(ImportProbeResult probeResult)
    {
        _probeWarnings = probeResult.Warnings;
        _latestFieldMappingSuggestions = probeResult.FieldMappingSuggestions;
        _hasProbeResult = true;
        _isProbeSupported = probeResult.IsSupported;
        _hasPreviewResult = false;
        _isPreviewCurrent = false;
        OnPropertyChanged(nameof(HasProbeResult));
        OnPropertyChanged(nameof(IsProbeSupported));
        OnPropertyChanged(nameof(HasPreviewResult));
        OnPropertyChanged(nameof(IsPreviewCurrent));

        DetectedSourceKindText = $"Detected source type: {ToDisplayName(probeResult.SourceKind)}";
        FileSupportStatusText = probeResult.IsSupported
            ? "Supported preview source."
            : "Unsupported for preview in this ticket.";
        ProbeDetailsText = BuildProbeDetails(probeResult);

        WorksheetOptions.Clear();
        foreach (var worksheetName in probeResult.WorksheetNames)
        {
            WorksheetOptions.Add(worksheetName);
        }

        SelectedWorksheetName = !string.IsNullOrWhiteSpace(probeResult.SelectedWorksheetName)
            ? probeResult.SelectedWorksheetName
            : WorksheetOptions.FirstOrDefault();

        if (!_hasManualImportDataKindSelection)
        {
            ApplyImportDataKindSelection(InferImportDataKind(), markManual: false, logSelection: false);
        }

        PreviewGrid.Clear();
        _latestColumns = Array.Empty<ImportPreviewColumn>();
        ColumnMappings.Clear();
        RefreshWarnings();
    }

    private void ApplySafePreviewFailure(string message)
    {
        GeneralErrorMessage = message;
        StatusMessage = "Review the support status, worksheet selection, and warnings, then try again.";
        _hasPreviewResult = false;
        _isPreviewCurrent = false;
        _previewWarnings = Array.Empty<ImportWarning>();
        OnPropertyChanged(nameof(HasPreviewResult));
        OnPropertyChanged(nameof(IsPreviewCurrent));
        PreviewGrid.Clear();
        ColumnMappings.Clear();
        RefreshWarnings();
        OnPropertyChanged(nameof(SummaryText));
    }

    private void Back()
    {
        if (CurrentStepIndex == 0)
        {
            return;
        }

        CurrentStepIndex--;
        StatusMessage = "Review the current step and continue when ready.";
    }

    private MessageImportFieldMapping[] BuildMessageImportFieldMappings()
    {
        return ColumnMappings
            .Where(static mapping => !string.IsNullOrWhiteSpace(mapping.SelectedSourceColumnNameOrNull))
            .Select(mapping => new MessageImportFieldMapping
            {
                DumpLensFieldName = mapping.DumpLensFieldName,
                SourceColumnName = mapping.SelectedSourceColumnNameOrNull,
                SourceColumnOrdinal = ResolveColumnOrdinal(mapping.SelectedSourceColumnNameOrNull)
            })
            .ToArray();
    }

    private ImportCallsRequest BuildImportCallsRequest(RegisterSourceResult registeredSource)
    {
        return new ImportCallsRequest
        {
            CaseId = _activeCase!.CaseId,
            SourceImportId = registeredSource.SourceImportId,
            CaseDatabasePath = _activeCase.DatabasePath,
            SourceFilePath = registeredSource.StoredFilePath,
            SourceKind = SelectedSourceTypeOption!.SourceKind,
            WorksheetName = SelectedWorksheetName,
            FieldMappings = BuildCallImportFieldMappings(),
            TimezoneAssumption = NormalizeOptional(TimezoneText),
            DefaultPlatformOrCarrier = NormalizeOptional(PlatformText),
            CorrelationId = _correlationId
        };
    }

    private CallImportFieldMapping[] BuildCallImportFieldMappings()
    {
        return ColumnMappings
            .Where(static mapping => !string.IsNullOrWhiteSpace(mapping.SelectedSourceColumnNameOrNull))
            .Select(mapping => new CallImportFieldMapping
            {
                DumpLensFieldName = mapping.DumpLensFieldName,
                SourceColumnName = mapping.SelectedSourceColumnNameOrNull,
                SourceColumnOrdinal = ResolveColumnOrdinal(mapping.SelectedSourceColumnNameOrNull)
            })
            .ToArray();
    }

    private ImportMessagesRequest BuildImportMessagesRequest(RegisterSourceResult registeredSource)
    {
        return new ImportMessagesRequest
        {
            CaseId = _activeCase!.CaseId,
            SourceImportId = registeredSource.SourceImportId,
            CaseDatabasePath = _activeCase.DatabasePath,
            SourceFilePath = registeredSource.StoredFilePath,
            SourceKind = SelectedSourceTypeOption!.SourceKind,
            WorksheetName = SelectedWorksheetName,
            FieldMappings = BuildMessageImportFieldMappings(),
            TimezoneAssumption = NormalizeOptional(TimezoneText),
            DefaultPlatform = NormalizeOptional(PlatformText),
            CorrelationId = _correlationId
        };
    }

    private static string BuildCaseLabel(CreateCaseResult activeCase)
    {
        return string.IsNullOrWhiteSpace(activeCase.CaseNumber)
            ? activeCase.Title
            : $"{activeCase.Title} ({activeCase.CaseNumber})";
    }

    private string BuildCompletedSummaryText()
    {
        var registeredSource = _registeredSourceResult;
        if (registeredSource is null)
        {
            return "Import completed, but the source summary is unavailable.";
        }

        return string.Join(
            Environment.NewLine,
            [
                "Import completed. The source was registered and the selected records were imported.",
                string.Empty,
                $"Case: {BuildCaseLabel(_activeCase!)}",
                $"Source import ID: {registeredSource.SourceImportId}",
                $"Source name: {registeredSource.SourceName}",
                $"Source type: {registeredSource.SourceType}",
                $"Platform: {registeredSource.Platform ?? NormalizeOptional(PlatformText) ?? "Not provided"}",
                $"File hash (SHA-256): {registeredSource.FileSha256}",
                $"File size: {registeredSource.FileSizeBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes",
                $"Imported record count: {_importedRecordCount.ToString("N0", CultureInfo.InvariantCulture)}",
                $"Warning count: {_persistedWarningCount.ToString("N0", CultureInfo.InvariantCulture)}",
                $"Audit event ID: {_importAuditEventId ?? "Unavailable"}",
                $"Copied file location: {ToSafeRelativePath(registeredSource.StoredFilePath)}",
                BuildWarningSummarySection()
            ]);
    }

    private string BuildIncompleteSummaryText()
    {
        var registeredSource = _registeredSourceResult;
        if (registeredSource is null)
        {
            return "Import did not complete. No source registration summary is available.";
        }

        return string.Join(
            Environment.NewLine,
            [
                "Import did not complete.",
                "The source registration was preserved, but the import workflow did not finish successfully.",
                string.Empty,
                $"Case: {BuildCaseLabel(_activeCase!)}",
                $"Source import ID: {registeredSource.SourceImportId}",
                $"Source name: {registeredSource.SourceName}",
                $"Source type: {registeredSource.SourceType}",
                $"Platform: {registeredSource.Platform ?? NormalizeOptional(PlatformText) ?? "Not provided"}",
                $"File hash (SHA-256): {registeredSource.FileSha256}",
                $"File size: {registeredSource.FileSizeBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes",
                $"Imported record count: {_importedRecordCount.ToString("N0", CultureInfo.InvariantCulture)}",
                $"Warning count: {_persistedWarningCount.ToString("N0", CultureInfo.InvariantCulture)}",
                $"Audit event ID: {_importAuditEventId ?? registeredSource.AuditEventId ?? "Unavailable"}",
                $"Copied file location: {ToSafeRelativePath(registeredSource.StoredFilePath)}",
                BuildWarningSummarySection()
            ]);
    }

    private string BuildPendingSummaryText()
    {
        var selectedKind = SelectedImportDataKindOption?.Label ?? "Messages";
        var caseLine = _activeCase is null
            ? "Case: Create or open a case before importing sources."
            : $"Case: {BuildCaseLabel(_activeCase)}";
        var platformLine = NormalizeOptional(PlatformText) is null
            ? "Platform or carrier: Not provided"
            : $"Platform or carrier: {NormalizeOptional(PlatformText)}";

        return string.Join(
            Environment.NewLine,
            [
                "Review this import setup before confirming.",
                string.Empty,
                caseLine,
                $"Selected source type: {SelectedSourceTypeOption?.Label ?? "Not selected"}",
                $"Import kind: {selectedKind}",
                platformLine,
                $"Worksheet: {SelectedWorksheetName ?? "Not selected"}",
                $"Timezone: {NormalizeOptional(TimezoneText) ?? "Not provided"}",
                $"Preview rows shown: {PreviewGrid.RowCount.ToString(CultureInfo.InvariantCulture)}",
                $"Warnings currently shown: {Warnings.Count.ToString(CultureInfo.InvariantCulture)}",
                string.Empty,
                "When you click Import, DumpLens will register and copy the selected source, compute SHA-256, write the source manifest and sha256.txt files, and then persist the mapped records into the active case.",
                BuildRequiredMappingStatusText()
            ]);
    }

    private string BuildProbeDetails(ImportProbeResult probeResult)
    {
        if (!probeResult.IsSupported)
        {
            return "The selected file is not supported for preview in this workflow. Review the warnings below for the safe failure reason.";
        }

        if (probeResult.SourceKind == ImportSourceKind.Xlsx)
        {
            return probeResult.WorksheetNames.Count == 0
                ? "Workbook inspection completed, but no worksheet names were found."
                : $"{probeResult.WorksheetNames.Count.ToString(CultureInfo.InvariantCulture)} worksheet(s) detected. Choose the worksheet you want to preview and import.";
        }

        return probeResult.DetectedDelimiter.HasValue
            ? $"Detected delimiter: {FormatDelimiter(probeResult.DetectedDelimiter.Value)}."
            : "CSV inspection completed without an explicit delimiter hint.";
    }

    private RegisterSourceRequest BuildRegisterSourceRequest(string fullPath, ImportDataKind dataKind)
    {
        return new RegisterSourceRequest
        {
            CaseId = _activeCase!.CaseId,
            CaseDatabasePath = _activeCase.DatabasePath,
            CasePackageRootPath = _activeCase.PackageRootPath,
            SelectedSourceFilePath = fullPath,
            SourceType = BuildSourceType(dataKind),
            Platform = NormalizeOptional(PlatformText),
            SourceMetadataJson = BuildSourceMetadataJson(dataKind),
            CorrelationId = _correlationId
        };
    }

    private static string BuildSourceMetadataJson(ImportDataKind dataKind, ImportSourceKind? sourceKind = null, string? worksheetName = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["import_kind"] = dataKind.ToString().ToLowerInvariant()
        };

        if (sourceKind.HasValue)
        {
            values["source_kind"] = sourceKind.Value.ToString().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(worksheetName))
        {
            values["worksheet_name"] = worksheetName.Trim();
        }

        var ordered = values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        return System.Text.Json.JsonSerializer.Serialize(ordered);
    }

    private string BuildSourceMetadataJson(ImportDataKind dataKind)
    {
        return BuildSourceMetadataJson(dataKind, SelectedSourceTypeOption?.SourceKind, SelectedWorksheetName);
    }

    private string BuildSourceType(ImportDataKind dataKind)
    {
        var sourceType = SelectedSourceTypeOption?.SourceKind switch
        {
            ImportSourceKind.Csv => "csv",
            ImportSourceKind.Xlsx => "xlsx",
            _ => "source"
        };

        var dataKindText = dataKind switch
        {
            ImportDataKind.Messages => "messages",
            ImportDataKind.Calls => "calls",
            _ => "records"
        };

        return $"{sourceType}_{dataKindText}";
    }

    private string BuildRequiredMappingStatusText()
    {
        var missingFields = GetMissingRequiredFieldDisplayNames().ToArray();
        if (missingFields.Length == 0)
        {
            return "Required mappings: complete.";
        }

        return $"Required mappings still needed: {string.Join(", ", missingFields)}.";
    }

    private string BuildUnsupportedFileMessage()
    {
        return "That file could not be previewed as the selected source type. Review the warnings and choose a supported CSV or XLSX file.";
    }

    private string BuildWarningSummarySection()
    {
        if (_latestImportedWarningSummaries.Count == 0)
        {
            return _persistedWarningCount == 0
                ? "Warnings: No import warnings were recorded."
                : "Warnings: Import warnings were recorded, but summary text is not available.";
        }

        var lines = new List<string>
        {
            "Warnings:"
        };

        foreach (var summary in _latestImportedWarningSummaries.Take(5))
        {
            lines.Add($"- {summary.Count.ToString("N0", CultureInfo.InvariantCulture)} warning(s): {summary.Message}");
        }

        if (_latestImportedWarningSummaries.Count > 5)
        {
            lines.Add($"- Additional warning summaries: {(_latestImportedWarningSummaries.Count - 5).ToString(CultureInfo.InvariantCulture)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void Cancel()
    {
        CloseWizard(_hasExecutedImport ? "closed_after_execution" : "canceled");
    }

    private void CloseWizard(string closeReason)
    {
        _logAction(
            WizardClosedOperation,
            _correlationId,
            "Import wizard closed.",
            new Dictionary<string, string>(CreateBaseLogFields(), StringComparer.Ordinal)
            {
                ["close_reason"] = closeReason,
                ["current_step"] = (CurrentStepIndex + 1).ToString(CultureInfo.InvariantCulture),
                ["preview_loaded"] = HasPreviewResult.ToString(CultureInfo.InvariantCulture),
                ["import_result_present"] = _hasImportResult.ToString(CultureInfo.InvariantCulture)
            });

        _onClose();
    }

    private IReadOnlyDictionary<string, string> CreateBaseLogFields()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["selected_source_type"] = SelectedSourceTypeOption?.SourceKind.ToString() ?? "none",
            ["selected_import_kind"] = SelectedImportDataKindOption?.DataKind.ToString().ToLowerInvariant() ?? "messages",
            ["active_case_present"] = (_activeCase is not null).ToString(CultureInfo.InvariantCulture),
            ["timezone_present"] = (!string.IsNullOrWhiteSpace(TimezoneText)).ToString(CultureInfo.InvariantCulture)
        };
    }

    private IReadOnlyDictionary<string, string> CreateFailureFields(string fullPath, string failureType)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["selected_source_type"] = SelectedSourceTypeOption?.SourceKind.ToString() ?? "none",
            ["selected_import_kind"] = SelectedImportDataKindOption?.DataKind.ToString().ToLowerInvariant() ?? "messages",
            ["file_extension"] = Path.GetExtension(fullPath),
            ["worksheet_selected"] = (!string.IsNullOrWhiteSpace(SelectedWorksheetName)).ToString(CultureInfo.InvariantCulture),
            ["failure_type"] = failureType
        };
    }

    private IReadOnlyDictionary<string, string> CreateFileOperationFields(string fullPath)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["selected_source_type"] = SelectedSourceTypeOption?.SourceKind.ToString() ?? "none",
            ["file_extension"] = Path.GetExtension(fullPath),
            ["file_path_present"] = (!string.IsNullOrWhiteSpace(fullPath)).ToString(CultureInfo.InvariantCulture)
        };
    }

    private IReadOnlyDictionary<string, string> CreateImportExecutionFields()
    {
        var fields = new Dictionary<string, string>(CreateBaseLogFields(), StringComparer.Ordinal)
        {
            ["has_probe_result"] = HasProbeResult.ToString(CultureInfo.InvariantCulture),
            ["has_preview_result"] = HasPreviewResult.ToString(CultureInfo.InvariantCulture),
            ["preview_current"] = IsPreviewCurrent.ToString(CultureInfo.InvariantCulture),
            ["required_mappings_missing"] = GetMissingRequiredFieldDisplayNames().Count().ToString(CultureInfo.InvariantCulture)
        };

        if (_activeCase is not null)
        {
            fields["case_id"] = _activeCase.CaseId;
        }

        return fields;
    }

    private IReadOnlyDictionary<string, string> CreateImportKindFields(ImportDataKind dataKind)
    {
        var fields = new Dictionary<string, string>(CreateBaseLogFields(), StringComparer.Ordinal)
        {
            ["selected_import_kind"] = dataKind.ToString().ToLowerInvariant()
        };

        if (_activeCase is not null)
        {
            fields["case_id"] = _activeCase.CaseId;
        }

        return fields;
    }

    private IReadOnlyDictionary<string, string> CreatePersistenceFailedFields(
        RegisterSourceResult registeredSource,
        ImportDataKind dataKind,
        string failureType)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["case_id"] = registeredSource.CaseId,
            ["source_import_id"] = registeredSource.SourceImportId,
            ["selected_import_kind"] = dataKind.ToString().ToLowerInvariant(),
            ["failure_type"] = failureType,
            ["warning_count"] = _latestImportedWarningSummaries.Sum(static summary => summary.Count).ToString(CultureInfo.InvariantCulture),
            ["file_size_bytes"] = registeredSource.FileSizeBytes.ToString(CultureInfo.InvariantCulture),
            ["hash_prefix"] = GetHashPrefix(registeredSource.FileSha256)
        };
    }

    private IReadOnlyDictionary<string, string> CreatePersistenceRequestedFields(
        RegisterSourceResult registeredSource,
        ImportDataKind dataKind)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["case_id"] = registeredSource.CaseId,
            ["source_import_id"] = registeredSource.SourceImportId,
            ["selected_import_kind"] = dataKind.ToString().ToLowerInvariant(),
            ["worksheet_selected"] = (!string.IsNullOrWhiteSpace(SelectedWorksheetName)).ToString(CultureInfo.InvariantCulture)
        };
    }

    private IReadOnlyDictionary<string, string> CreatePersistenceSucceededFields(
        RegisterSourceResult registeredSource,
        ImportDataKind dataKind,
        int importedRecordCount,
        int warningCount,
        string? auditEventId)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["case_id"] = registeredSource.CaseId,
            ["source_import_id"] = registeredSource.SourceImportId,
            ["selected_import_kind"] = dataKind.ToString().ToLowerInvariant(),
            ["imported_record_count"] = importedRecordCount.ToString(CultureInfo.InvariantCulture),
            ["warning_count"] = warningCount.ToString(CultureInfo.InvariantCulture),
            ["audit_event_id_present"] = (!string.IsNullOrWhiteSpace(auditEventId)).ToString(CultureInfo.InvariantCulture)
        };
    }

    private IReadOnlyDictionary<string, string> CreatePreviewRequestFields(string fullPath)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["selected_source_type"] = SelectedSourceTypeOption?.SourceKind.ToString() ?? "none",
            ["file_extension"] = Path.GetExtension(fullPath),
            ["worksheet_selected"] = (!string.IsNullOrWhiteSpace(SelectedWorksheetName)).ToString(CultureInfo.InvariantCulture),
            ["requested_row_count"] = PreviewRowCount.ToString(CultureInfo.InvariantCulture)
        };
    }

    private IReadOnlyDictionary<string, string> CreatePreviewSuccessFields(string fullPath, ImportPreviewResult previewResult)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["selected_source_type"] = SelectedSourceTypeOption?.SourceKind.ToString() ?? "none",
            ["file_extension"] = Path.GetExtension(fullPath),
            ["worksheet_count"] = WorksheetOptions.Count.ToString(CultureInfo.InvariantCulture),
            ["warning_count"] = previewResult.Warnings.Count.ToString(CultureInfo.InvariantCulture),
            ["returned_row_count"] = previewResult.ReturnedRowCount.ToString(CultureInfo.InvariantCulture),
            ["mapping_count"] = previewResult.FieldMappingSuggestions.Count.ToString(CultureInfo.InvariantCulture)
        };
    }

    private IReadOnlyDictionary<string, string> CreateSourceRegistrationSucceededFields(
        RegisterSourceResult registeredSource,
        ImportDataKind dataKind)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["case_id"] = registeredSource.CaseId,
            ["source_import_id"] = registeredSource.SourceImportId,
            ["selected_import_kind"] = dataKind.ToString().ToLowerInvariant(),
            ["file_size_bytes"] = registeredSource.FileSizeBytes.ToString(CultureInfo.InvariantCulture),
            ["hash_prefix"] = GetHashPrefix(registeredSource.FileSha256)
        };
    }

    private static IReadOnlyList<ImportDataKindOptionViewModel> CreateImportDataKindOptions()
    {
        return
        [
            new ImportDataKindOptionViewModel(
                ImportDataKind.Messages,
                "Messages",
                "Persist mapped message-style rows to messages, message recipients, identities, source artifacts, and import warnings."),
            new ImportDataKindOptionViewModel(
                ImportDataKind.Calls,
                "Calls",
                "Persist mapped call-log-style rows to calls, identities, source artifacts, and import warnings.")
        ];
    }

    private static IReadOnlyList<ImportWizardStepViewModel> CreateSteps()
    {
        return
        [
            new ImportWizardStepViewModel(1, "Choose source type", "Pick the supported source type for preview and import."),
            new ImportWizardStepViewModel(2, "Select file", "Enter a file path, inspect support status, and review worksheet options for XLSX."),
            new ImportWizardStepViewModel(3, "Confirm import setup", "Choose the import kind, set platform/carrier text if needed, and keep any optional placeholder labels for review."),
            new ImportWizardStepViewModel(4, "Preview data", "Load preview rows and confirm the source structure before importing."),
            new ImportWizardStepViewModel(5, "Map columns", "Review and adjust the source-to-DumpLens field mappings."),
            new ImportWizardStepViewModel(6, "Confirm timestamp/timezone", "Confirm the timestamp and timezone context for import."),
            new ImportWizardStepViewModel(7, "Review validation warnings", "Inspect warnings and extraction limitations before importing."),
            new ImportWizardStepViewModel(8, "Import summary", "Review the final import setup and confirm to register, copy, hash, and persist the source.")
        ];
    }

    private List<ImportSourceTypeOptionViewModel> CreateSourceTypeOptions()
    {
        return
        [
            new ImportSourceTypeOptionViewModel(
                ImportSourceKind.Csv,
                "CSV",
                "Use this for comma-, tab-, pipe-, or semicolon-delimited exports that the CSV preview path can inspect safely.",
                ".csv, .txt",
                _importers.ContainsKey(ImportSourceKind.Csv)),
            new ImportSourceTypeOptionViewModel(
                ImportSourceKind.Xlsx,
                "XLSX",
                "Use this for workbook-style exports when you need worksheet selection before previewing the tabular data.",
                ".xlsx",
                _importers.ContainsKey(ImportSourceKind.Xlsx))
        ];
    }

    private static string FormatDelimiter(char delimiter)
    {
        return delimiter switch
        {
            '\t' => "Tab",
            ',' => "Comma",
            ';' => "Semicolon",
            '|' => "Pipe",
            _ => delimiter.ToString(CultureInfo.InvariantCulture)
        };
    }

    private IEnumerable<string> GetMissingRequiredFieldDisplayNames()
    {
        var selectedKind = SelectedImportDataKindOption?.DataKind ?? ImportDataKind.Messages;
        return selectedKind switch
        {
            ImportDataKind.Messages => GetMissingFieldDisplayNames(
                ImportFieldNames.Timestamp,
                ImportFieldNames.Sender,
                ImportFieldNames.Recipient,
                ImportFieldNames.MessageBody),
            ImportDataKind.Calls => GetMissingCallFieldDisplayNames(),
            _ => Array.Empty<string>()
        };
    }

    private IEnumerable<string> GetMissingFieldDisplayNames(params string[] requiredFieldNames)
    {
        foreach (var fieldName in requiredFieldNames)
        {
            var mapping = ColumnMappings.FirstOrDefault(candidate =>
                string.Equals(candidate.DumpLensFieldName, fieldName, StringComparison.Ordinal));
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.SelectedSourceColumnNameOrNull))
            {
                yield return ToFieldDisplayName(fieldName);
            }
        }
    }

    private IEnumerable<string> GetMissingCallFieldDisplayNames()
    {
        if (!HasMappedField(ImportFieldNames.Timestamp))
        {
            yield return "Timestamp";
        }

        if (!HasMappedField(ImportFieldNames.Caller) && !HasMappedField(ImportFieldNames.Sender))
        {
            yield return "Caller";
        }

        if (!HasMappedField(ImportFieldNames.Callee) && !HasMappedField(ImportFieldNames.Recipient))
        {
            yield return "Callee";
        }
    }

    private static string GetHashPrefix(string hash)
    {
        return hash.Length <= 12
            ? hash
            : hash[..12];
    }

    private bool HasMappedField(string fieldName)
    {
        var mapping = ColumnMappings.FirstOrDefault(candidate =>
            string.Equals(candidate.DumpLensFieldName, fieldName, StringComparison.Ordinal));
        return mapping is not null && !string.IsNullOrWhiteSpace(mapping.SelectedSourceColumnNameOrNull);
    }

    private ImportDataKind InferImportDataKind()
    {
        if (!string.IsNullOrWhiteSpace(SelectedWorksheetName) &&
            SelectedWorksheetName.Contains("call", StringComparison.OrdinalIgnoreCase))
        {
            return ImportDataKind.Calls;
        }

        var hasCallerSignal = HasFieldSuggestionOrColumn(CallImportFieldNames.Caller) || HasFieldSuggestionOrColumn(CallImportFieldNames.SenderAlias);
        var hasCalleeSignal = HasFieldSuggestionOrColumn(CallImportFieldNames.Callee) || HasFieldSuggestionOrColumn(CallImportFieldNames.RecipientAlias);
        var hasMessageBodySignal = HasFieldSuggestionOrColumn(ImportFieldNames.MessageBody);
        if (hasCallerSignal && hasCalleeSignal && !hasMessageBodySignal)
        {
            return ImportDataKind.Calls;
        }

        return ImportDataKind.Messages;
    }

    private void InvalidateProbeAndPreview(bool resetWorksheetSelection)
    {
        _hasProbeResult = false;
        _hasPreviewResult = false;
        _isPreviewCurrent = false;
        _isProbeSupported = false;
        _latestColumns = Array.Empty<ImportPreviewColumn>();
        _latestFieldMappingSuggestions = Array.Empty<ImportFieldMappingSuggestion>();
        _latestImportedWarningSummaries = Array.Empty<ImportWarningSummary>();
        _probeWarnings = Array.Empty<ImportWarning>();
        _previewWarnings = Array.Empty<ImportWarning>();
        _registeredSourceResult = null;
        _importAuditEventId = null;
        _importedRecordCount = 0;
        _persistedWarningCount = 0;
        _hasImportResult = false;
        _hasExecutedImport = false;
        _importCompleted = false;
        _summaryCloseOnly = false;
        _hasManualImportDataKindSelection = false;
        ApplyImportDataKindSelection(ImportDataKind.Messages, markManual: false, logSelection: false);

        if (resetWorksheetSelection)
        {
            WorksheetOptions.Clear();
            SelectedWorksheetName = null;
        }

        PreviewGrid.Clear();
        ColumnMappings.Clear();
        Warnings.Clear();
        SelectedWarning = null;
        DetectedSourceKindText = "No file inspected yet.";
        FileSupportStatusText = "Select a source type and inspect a file to continue.";
        ProbeDetailsText = "Preview and probe are available before import. The final step registers, copies, hashes, and persists the source only after you confirm.";

        OnPropertyChanged(nameof(HasProbeResult));
        OnPropertyChanged(nameof(HasPreviewResult));
        OnPropertyChanged(nameof(IsPreviewCurrent));
        OnPropertyChanged(nameof(IsProbeSupported));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(WarningSummaryText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(NextButtonText));
    }

    private bool HasFieldSuggestionOrColumn(string fieldName)
    {
        if (_latestFieldMappingSuggestions.Any(suggestion =>
                string.Equals(suggestion.DumpLensFieldName, fieldName, StringComparison.Ordinal)))
        {
            return true;
        }

        return _latestColumns.Any(column =>
            string.Equals(column.SourceColumnName, fieldName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<ImportWarningSummary>> LoadImportedWarningSummariesAsync(RegisterSourceResult registeredSource)
    {
        try
        {
            return await _importWarningSummaryReader.GetSummariesAsync(
                    _activeCase!.DatabasePath,
                    registeredSource.SourceImportId,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            return Array.Empty<ImportWarningSummary>();
        }
    }

    private static void LogWorkflowFailed(
        Action<string, string, string, IReadOnlyDictionary<string, string>?> logAction,
        string correlationId,
        string message,
        IReadOnlyDictionary<string, string> fields)
    {
        logAction(
            ImportWorkflowFailedOperation,
            correlationId,
            message,
            fields);
    }

    private void LogWorkflowFailed(string failureStage, string failureType)
    {
        var fields = new Dictionary<string, string>(CreateImportExecutionFields(), StringComparer.Ordinal)
        {
            ["failure_stage"] = failureStage,
            ["failure_type"] = failureType
        };

        LogWorkflowFailed(
            _logAction,
            _correlationId,
            "Import workflow failed.",
            fields);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private void RaiseCommandStateChanged()
    {
        _nextCommand.RaiseCanExecuteChanged();
        _backCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
        _probeFileCommand.RaiseCanExecuteChanged();
        _refreshPreviewCommand.RaiseCanExecuteChanged();
        _browsePlaceholderCommand.RaiseCanExecuteChanged();
    }

    private void RefreshMappings()
    {
        ColumnMappings.Clear();

        var availableColumns = _latestColumns
            .Select(static column => column.SourceColumnName)
            .ToList();
        var suggestionsByField = _latestFieldMappingSuggestions
            .GroupBy(static suggestion => suggestion.DumpLensFieldName, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        foreach (var fieldName in ImportFieldNames.All)
        {
            suggestionsByField.TryGetValue(fieldName, out var suggestion);
            ColumnMappings.Add(new ImportColumnMappingViewModel(
                fieldName,
                ToFieldDisplayName(fieldName),
                availableColumns,
                suggestion));
        }
    }

    private void RefreshWarnings()
    {
        var combinedWarnings = new List<ImportWarningViewModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var warning in _probeWarnings.Concat(_previewWarnings))
        {
            var key = string.Join(
                "|",
                warning.Code,
                warning.WorksheetName ?? string.Empty,
                warning.RowNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                warning.ColumnName ?? string.Empty,
                warning.Message);

            if (seen.Add(key))
            {
                combinedWarnings.Add(new ImportWarningViewModel(warning));
            }
        }

        Warnings.Clear();
        foreach (var warning in combinedWarnings)
        {
            Warnings.Add(warning);
        }

        SelectedWarning = Warnings.FirstOrDefault();
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(WarningSummaryText));
        OnPropertyChanged(nameof(SummaryText));
    }

    private int? ResolveColumnOrdinal(string? sourceColumnName)
    {
        if (string.IsNullOrWhiteSpace(sourceColumnName))
        {
            return null;
        }

        var matchingColumn = _latestColumns.FirstOrDefault(column =>
            string.Equals(column.SourceColumnName, sourceColumnName, StringComparison.Ordinal));
        return matchingColumn?.Ordinal;
    }

    private ISourceImporter? ResolveSelectedImporter()
    {
        return SelectedSourceTypeOption is not null &&
               _importers.TryGetValue(SelectedSourceTypeOption.SourceKind, out var importer)
            ? importer
            : null;
    }

    private void ShowBrowsePlaceholder()
    {
        GeneralErrorMessage = string.Empty;
        StatusMessage = "Browse is a placeholder in this ticket. Enter an absolute CSV or XLSX file path directly.";
    }

    private static string ToDisplayName(ImportSourceKind sourceKind)
    {
        return sourceKind switch
        {
            ImportSourceKind.Csv => "CSV",
            ImportSourceKind.Xlsx => "XLSX",
            _ => sourceKind.ToString()
        };
    }

    private static string ToFieldDisplayName(string fieldName)
    {
        return fieldName switch
        {
            ImportFieldNames.Timestamp => "Timestamp",
            ImportFieldNames.Sender => "Sender",
            ImportFieldNames.Recipient => "Recipient",
            ImportFieldNames.MessageBody => "Message body",
            ImportFieldNames.Platform => "Platform",
            ImportFieldNames.Direction => "Direction",
            ImportFieldNames.ThreadId => "Thread ID",
            ImportFieldNames.MessageId => "Message ID",
            ImportFieldNames.Attachment => "Attachment",
            ImportFieldNames.Caller => "Caller",
            ImportFieldNames.Callee => "Callee",
            ImportFieldNames.Duration => "Duration",
            ImportFieldNames.CallType => "Call type",
            CallImportFieldNames.PlatformOrCarrier => "Platform or carrier",
            _ => fieldName
        };
    }

    private string ToSafeRelativePath(string fullPath)
    {
        if (_activeCase is null)
        {
            return Path.GetFileName(fullPath);
        }

        try
        {
            var relativePath = Path.GetRelativePath(_activeCase.PackageRootPath, fullPath);
            return relativePath.Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(fullPath);
        }
    }

    private bool TryNormalizeAbsoluteFilePath(
        string? filePath,
        out string fullPath,
        out string validationMessage)
    {
        fullPath = string.Empty;
        validationMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            validationMessage = "Enter an absolute file path before requesting a preview.";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(filePath.Trim());
            if (!Path.IsPathRooted(fullPath))
            {
                validationMessage = "Enter an absolute file path before requesting a preview.";
                return false;
            }

            return true;
        }
        catch
        {
            validationMessage = "Enter a valid absolute file path before requesting a preview.";
            return false;
        }
    }

    private void UpdateStepState()
    {
        for (var index = 0; index < Steps.Count; index++)
        {
            Steps[index].IsCurrent = index == CurrentStepIndex;
            Steps[index].IsCompleted = index < CurrentStepIndex || (_summaryCloseOnly && index == CurrentStepIndex);
        }
    }

    private bool ValidateRequiredMappings(out string message)
    {
        var missingFields = GetMissingRequiredFieldDisplayNames().ToArray();
        if (missingFields.Length == 0)
        {
            message = string.Empty;
            return true;
        }

        var selectedKind = SelectedImportDataKindOption?.Label?.ToLowerInvariant() ?? "messages";
        message = $"Map the required fields before importing {selectedKind}: {string.Join(", ", missingFields)}.";
        return false;
    }

    private bool ValidateSourceTypeSelection()
    {
        if (SelectedSourceTypeOption is null)
        {
            GeneralErrorMessage = "Choose a source type before continuing.";
            return false;
        }

        if (!SelectedSourceTypeOption.IsAvailable)
        {
            GeneralErrorMessage = $"The {SelectedSourceTypeOption.Label} preview path is not configured in this shell.";
            return false;
        }

        return true;
    }

    private async Task<bool> VerifyAuditChainAsync(string caseId)
    {
        if (_auditLoggerFactory is null || _activeCase is null)
        {
            return true;
        }

        var connectionString = string.Create(
            CultureInfo.InvariantCulture,
            $"Data Source={_activeCase.DatabasePath};Foreign Keys=True;Pooling=False");

        var verification = await _auditLoggerFactory(connectionString).VerifyChainAsync(
                caseId,
                _correlationId,
                CancellationToken.None)
            .ConfigureAwait(false);

        return verification.IsValid;
    }

    private sealed class EmptyImportWarningSummaryReader : IImportWarningSummaryReader
    {
        public Task<IReadOnlyList<ImportWarningSummary>> GetSummariesAsync(
            string caseDatabasePath,
            string sourceImportId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ImportWarningSummary>>(Array.Empty<ImportWarningSummary>());
        }
    }

    private sealed class UnavailableCallImportService : ICallImportService
    {
        public Task<ImportCallsResult> ImportAsync(
            ImportCallsRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No call import service is configured for this wizard instance.");
        }
    }

    private sealed class UnavailableMessageImportService : IMessageImportService
    {
        public Task<ImportMessagesResult> ImportAsync(
            ImportMessagesRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No message import service is configured for this wizard instance.");
        }
    }

    private sealed class UnavailableSourceRegistrationService : ISourceRegistrationService
    {
        public Task<RegisterSourceResult> RegisterAsync(
            RegisterSourceRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No source registration service is configured for this wizard instance.");
        }
    }
}
