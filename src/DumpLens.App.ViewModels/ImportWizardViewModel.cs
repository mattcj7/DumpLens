using System.Collections.ObjectModel;
using System.Globalization;
using DumpLens.Application.Imports;
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
    private const int PreviewRowCount = 10;

    private static readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> NoOpLogAction = static (_, _, _, _) => { };

    private readonly IReadOnlyDictionary<ImportSourceKind, ISourceImporter> _importers;
    private readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> _logAction;
    private readonly Action _onClose;
    private readonly AsyncRelayCommand _nextCommand;
    private readonly RelayCommand _backCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly AsyncRelayCommand _probeFileCommand;
    private readonly AsyncRelayCommand _refreshPreviewCommand;
    private readonly RelayCommand _browsePlaceholderCommand;
    private readonly string _correlationId;
    private IReadOnlyList<ImportPreviewColumn> _latestColumns;
    private IReadOnlyList<ImportFieldMappingSuggestion> _latestFieldMappingSuggestions;
    private IReadOnlyList<ImportWarning> _probeWarnings;
    private IReadOnlyList<ImportWarning> _previewWarnings;
    private ImportSourceTypeOptionViewModel? _selectedSourceTypeOption;
    private ImportWarningViewModel? _selectedWarning;
    private bool _hasPreviewResult;
    private bool _hasProbeResult;
    private string _detectedSourceKindText;
    private string _filePath;
    private string _fileSupportStatusText;
    private string _generalErrorMessage;
    private int _currentStepIndex;
    private bool _isBusy;
    private bool _isPreviewCurrent;
    private bool _isProbeSupported;
    private string _probeDetailsText;
    private string _sourceAccountText;
    private string _sourceDeviceText;
    private string _sourceOwnerText;
    private string _statusMessage;
    private string? _selectedWorksheetName;
    private string _timezoneText;

    public ImportWizardViewModel(
        IEnumerable<ISourceImporter> sourceImporters,
        Action onClose,
        Action<string, string, string, IReadOnlyDictionary<string, string>?>? logAction = null,
        string? defaultTimezone = null)
    {
        ArgumentNullException.ThrowIfNull(sourceImporters);

        _onClose = onClose ?? throw new ArgumentNullException(nameof(onClose));
        _logAction = logAction ?? NoOpLogAction;
        _correlationId = Guid.NewGuid().ToString("N");
        _importers = sourceImporters
            .GroupBy(static importer => importer.SourceKind)
            .ToDictionary(static group => group.Key, static group => group.First());
        _latestColumns = Array.Empty<ImportPreviewColumn>();
        _latestFieldMappingSuggestions = Array.Empty<ImportFieldMappingSuggestion>();
        _probeWarnings = Array.Empty<ImportWarning>();
        _previewWarnings = Array.Empty<ImportWarning>();
        _detectedSourceKindText = "No file inspected yet.";
        _filePath = string.Empty;
        _fileSupportStatusText = "Select a source type and inspect a file to continue.";
        _generalErrorMessage = string.Empty;
        _probeDetailsText = "The wizard uses preview-only inspection in this ticket. No case records, source artifacts, or evidence copies will be created.";
        _sourceAccountText = string.Empty;
        _sourceDeviceText = string.Empty;
        _sourceOwnerText = string.Empty;
        _statusMessage = "Choose a source type to begin.";
        _timezoneText = string.IsNullOrWhiteSpace(defaultTimezone) ? TimeZoneInfo.Local.Id : defaultTimezone.Trim();

        Steps = new ObservableCollection<ImportWizardStepViewModel>(CreateSteps());
        SourceTypeOptions = new ObservableCollection<ImportSourceTypeOptionViewModel>(CreateSourceTypeOptions());
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

    public string NextButtonText => IsSummaryStep ? "Close" : "Next";

    public AsyncRelayCommand NextCommand => _nextCommand;

    public ImportPreviewGridViewModel PreviewGrid { get; }

    public string ProbeDetailsText
    {
        get => _probeDetailsText;
        private set => SetProperty(ref _probeDetailsText, value);
    }

    public AsyncRelayCommand ProbeFileCommand => _probeFileCommand;

    public AsyncRelayCommand RefreshPreviewCommand => _refreshPreviewCommand;

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

    public string SummaryText =>
        $"Preview complete. Persistence will be added in a later ticket.{Environment.NewLine}{Environment.NewLine}" +
        $"Source type: {SelectedSourceTypeOption?.Label ?? "Not selected"}{Environment.NewLine}" +
        $"Preview rows shown: {PreviewGrid.RowCount.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}" +
        $"Warnings shown: {Warnings.Count.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}" +
        $"Timezone confirmation: {(string.IsNullOrWhiteSpace(TimezoneText) ? "Not entered" : TimezoneText)}{Environment.NewLine}" +
        "This wizard does not create persons, devices, platform accounts, source imports, source artifacts, messages, calls, identities, warnings, or any other persistent records yet.";

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
                StatusMessage = "Enter placeholder source owner, device, and account text if helpful for review.";
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
                StatusMessage = "Confirm the timestamp and timezone context before any future persistence ticket.";
                return;

            case 5:
                CurrentStepIndex = 6;
                StatusMessage = "Review warnings and any extraction limitations before closing the preview-only flow.";
                return;

            case 6:
                CurrentStepIndex = 7;
                StatusMessage = "This final step is a placeholder only. No data will be imported yet.";
                OnPropertyChanged(nameof(SummaryText));
                return;

            case 7:
                CloseWizard("completed_preview_only");
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
                StatusMessage = "File inspection complete. Continue to source placeholders or preview data.";
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
                : "Preview loaded. Review rows, mappings, timezone, and warnings before closing the wizard.";

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

    private void Cancel()
    {
        CloseWizard("canceled");
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
                ["preview_loaded"] = HasPreviewResult.ToString(CultureInfo.InvariantCulture)
            });

        _onClose();
    }

    private IReadOnlyDictionary<string, string> CreateBaseLogFields()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["selected_source_type"] = SelectedSourceTypeOption?.SourceKind.ToString() ?? "none",
            ["timezone_present"] = (!string.IsNullOrWhiteSpace(TimezoneText)).ToString(CultureInfo.InvariantCulture)
        };
    }

    private IReadOnlyDictionary<string, string> CreateFailureFields(string fullPath, string failureType)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["selected_source_type"] = SelectedSourceTypeOption?.SourceKind.ToString() ?? "none",
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

    private static IReadOnlyList<ImportWizardStepViewModel> CreateSteps()
    {
        return
        [
            new ImportWizardStepViewModel(1, "Choose source type", "Pick the preview source type this wizard should use."),
            new ImportWizardStepViewModel(2, "Select file", "Enter a file path, inspect support status, and review worksheet options for XLSX."),
            new ImportWizardStepViewModel(3, "Assign source placeholders", "Capture owner, device, and account notes as manual placeholders only."),
            new ImportWizardStepViewModel(4, "Preview data", "Load preview rows and the detected source structure without persisting anything."),
            new ImportWizardStepViewModel(5, "Map columns", "Review and adjust the suggested DumpLens field mappings."),
            new ImportWizardStepViewModel(6, "Confirm timestamp/timezone", "Confirm the timestamp and timezone context for later persistence tickets."),
            new ImportWizardStepViewModel(7, "Review validation warnings", "Inspect warnings and extraction limitations before closing the preview-only workflow."),
            new ImportWizardStepViewModel(8, "Import summary placeholder", "Review the preview-only summary. Persistence is intentionally not implemented yet.")
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

    private string BuildProbeDetails(ImportProbeResult probeResult)
    {
        if (!probeResult.IsSupported)
        {
            return "The selected file is not supported for preview in this ticket. Review the warnings below for the safe failure reason.";
        }

        if (probeResult.SourceKind == ImportSourceKind.Xlsx)
        {
            return probeResult.WorksheetNames.Count == 0
                ? "Workbook inspection completed, but no worksheet names were found."
                : $"{probeResult.WorksheetNames.Count.ToString(CultureInfo.InvariantCulture)} worksheet(s) detected. Choose a worksheet before refreshing preview if needed.";
        }

        return probeResult.DetectedDelimiter.HasValue
            ? $"Detected delimiter: {FormatDelimiter(probeResult.DetectedDelimiter.Value)}."
            : "CSV inspection completed without an explicit delimiter hint.";
    }

    private string BuildUnsupportedFileMessage()
    {
        return "That file could not be previewed as the selected source type. Review the warnings and choose a supported CSV or XLSX file.";
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

    private void InvalidateProbeAndPreview(bool resetWorksheetSelection)
    {
        _hasProbeResult = false;
        _hasPreviewResult = false;
        _isPreviewCurrent = false;
        _isProbeSupported = false;
        _latestColumns = Array.Empty<ImportPreviewColumn>();
        _latestFieldMappingSuggestions = Array.Empty<ImportFieldMappingSuggestion>();
        _probeWarnings = Array.Empty<ImportWarning>();
        _previewWarnings = Array.Empty<ImportWarning>();

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
        ProbeDetailsText = "The wizard uses preview-only inspection in this ticket. No case records, source artifacts, or evidence copies will be created.";

        OnPropertyChanged(nameof(HasProbeResult));
        OnPropertyChanged(nameof(HasPreviewResult));
        OnPropertyChanged(nameof(IsPreviewCurrent));
        OnPropertyChanged(nameof(IsProbeSupported));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(WarningSummaryText));
        OnPropertyChanged(nameof(SummaryText));
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
            _ => fieldName
        };
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
            Steps[index].IsCompleted = index < CurrentStepIndex;
        }
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
}
