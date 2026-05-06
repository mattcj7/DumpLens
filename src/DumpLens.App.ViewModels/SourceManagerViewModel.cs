using System.Collections.ObjectModel;
using System.Globalization;
using DumpLens.Application.Cases;
using DumpLens.Application.Sources;
using DumpLens.Application.SourceReferences;

namespace DumpLens.App.ViewModels;

public sealed class SourceManagerViewModel : WorkspaceViewModelBase
{
    private const string ActiveCaseMissingOperation = "source_manager_active_case_missing";
    private const string SourceListLoadFailedOperation = "source_manager_source_list_load_failed";
    private const string SourceListLoadRequestedOperation = "source_manager_source_list_load_requested";
    private const string SourceListLoadSucceededOperation = "source_manager_source_list_load_succeeded";
    private const string SourceManagerOpenedOperation = "source_manager_opened";
    private const string SourceSelectedOperation = "source_manager_source_selected";
    private static readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> NoOpLogAction = static (_, _, _, _) => { };

    private readonly CreateCaseResult? _activeCase;
    private readonly string _correlationId;
    private readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> _logAction;
    private readonly ISourceManagerService _sourceManagerService;
    private readonly ISourceReferenceReader _sourceReferenceReader;
    private SourceReferenceInspectorViewModel _currentDetail;
    private string? _errorMessage;
    private int _inspectorLoadVersion;
    private bool _isLoading;
    private SourceListItemViewModel? _selectedSource;
    private string _statusMessage;

    public SourceManagerViewModel(
        CreateCaseResult? activeCase,
        ISourceManagerService sourceManagerService,
        ISourceReferenceReader sourceReferenceReader,
        Action<string, string, string, IReadOnlyDictionary<string, string>?>? logAction = null)
        : base(
            "Sources",
            "Review registered sources, safe file metadata, import status, counts, and warning summaries for the active case.")
    {
        _activeCase = activeCase;
        _sourceManagerService = sourceManagerService ?? throw new ArgumentNullException(nameof(sourceManagerService));
        _sourceReferenceReader = sourceReferenceReader ?? throw new ArgumentNullException(nameof(sourceReferenceReader));
        _logAction = logAction ?? NoOpLogAction;
        _correlationId = Guid.NewGuid().ToString("N");
        _statusMessage = activeCase is null
            ? "Create or open a case to view imported sources."
            : "Loading sources for the active case.";
        _currentDetail = activeCase is null
            ? SourceReferenceInspectorViewModel.CreateActiveCaseMissing()
            : SourceReferenceInspectorViewModel.CreateNoSelection();
        Sources = new ObservableCollection<SourceListItemViewModel>();

        _logAction(
            SourceManagerOpenedOperation,
            _correlationId,
            "Source manager opened.",
            CreateBaseFields());

        if (_activeCase is null)
        {
            _logAction(
                ActiveCaseMissingOperation,
                _correlationId,
                "Source manager requires an active case.",
                CreateBaseFields());
            return;
        }

        _ = LoadAsync();
    }

    public SourceReferenceInspectorViewModel CurrentDetail
    {
        get => _currentDetail;
        private set => SetProperty(ref _currentDetail, value);
    }

    public string EmptyStateMessage => HasActiveCase
        ? "No sources are registered for this case yet."
        : "Create or open a case to view imported sources.";

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasActiveCase => _activeCase is not null;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasSources => Sources.Count > 0;

    public bool IsEmptyStateVisible => !IsLoading && !HasError && !HasSources;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmptyStateVisible));
            }
        }
    }

    public SourceListItemViewModel? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (!SetProperty(ref _selectedSource, value))
            {
                return;
            }

            _ = LoadSelectedSourceDetailAsync(value);
        }
    }

    public int SourceCount => Sources.Count;

    public ObservableCollection<SourceListItemViewModel> Sources { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int TotalRecordCount => Sources.Sum(static item => item.RecordCount);

    public int TotalWarningCount => Sources.Sum(static item => item.WarningCount);

    private async Task LoadAsync()
    {
        if (_activeCase is null)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = "Loading sources for the active case.";
        CurrentDetail = SourceReferenceInspectorViewModel.CreateNoSelection();

        _logAction(
            SourceListLoadRequestedOperation,
            _correlationId,
            "Source list load requested.",
            CreateBaseFields());

        try
        {
            var summaries = await _sourceManagerService.GetSummariesAsync(
                    new LoadSourceImportSummariesRequest
                    {
                        CaseId = _activeCase.CaseId,
                        CaseDatabasePath = _activeCase.DatabasePath,
                        CasePackageRootPath = _activeCase.PackageRootPath
                    })
                ;

            ReplaceSources(summaries.Select(static summary => new SourceListItemViewModel(summary)));
            StatusMessage = summaries.Count == 0
                ? "No sources are registered for this case yet."
                : $"{summaries.Count.ToString(CultureInfo.InvariantCulture)} sources loaded.";

            _logAction(
                SourceListLoadSucceededOperation,
                _correlationId,
                "Source list load succeeded.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["source_count"] = summaries.Count.ToString(CultureInfo.InvariantCulture),
                    ["total_record_count"] = TotalRecordCount.ToString(CultureInfo.InvariantCulture),
                    ["total_warning_count"] = TotalWarningCount.ToString(CultureInfo.InvariantCulture)
                });

            SelectedSource = Sources.FirstOrDefault();
            if (SelectedSource is null)
            {
                CurrentDetail = SourceReferenceInspectorViewModel.CreateNoSelection();
            }
        }
        catch (Exception ex)
        {
            ReplaceSources(Array.Empty<SourceListItemViewModel>());
            SelectedSource = null;
            ErrorMessage = "Sources could not be loaded. Check the case package and try again.";
            StatusMessage = ErrorMessage;
            CurrentDetail = SourceReferenceInspectorViewModel.CreateLoadFailure();

            _logAction(
                SourceListLoadFailedOperation,
                _correlationId,
                "Source list load failed.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["failure_type"] = ex.GetType().Name
                });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSelectedSourceDetailAsync(SourceListItemViewModel? selectedSource)
    {
        if (_activeCase is null)
        {
            CurrentDetail = SourceReferenceInspectorViewModel.CreateActiveCaseMissing();
            return;
        }

        if (selectedSource is null)
        {
            CurrentDetail = SourceReferenceInspectorViewModel.CreateNoSelection();
            return;
        }

        var requestVersion = Interlocked.Increment(ref _inspectorLoadVersion);
        CurrentDetail = SourceReferenceInspectorViewModel.CreateLoading();
        _logAction(
            SourceSelectedOperation,
            _correlationId,
            "Source selected.",
            new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
            {
                ["source_import_id"] = selectedSource.SourceImportId
            });

        _logAction(
            "source_reference_inspector_requested",
            _correlationId,
            "Source reference inspector requested.",
            new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
            {
                ["source_import_id"] = selectedSource.SourceImportId,
                ["source_artifact_id"] = "-",
                ["message_id"] = "-"
            });

        try
        {
            var detail = await _sourceReferenceReader.LoadAsync(
                    new LoadSourceReferenceRequest
                    {
                        CaseId = _activeCase.CaseId,
                        CaseDatabasePath = _activeCase.DatabasePath,
                        CasePackageRootPath = _activeCase.PackageRootPath,
                        SourceImportId = selectedSource.SourceImportId,
                        CorrelationId = _correlationId
                    })
                .ConfigureAwait(false);

            if (requestVersion != _inspectorLoadVersion || !ReferenceEquals(SelectedSource, selectedSource))
            {
                return;
            }

            if (detail is null)
            {
                CurrentDetail = SourceReferenceInspectorViewModel.CreateLoadFailure();
                _logAction(
                    "source_reference_inspector_missing",
                    _correlationId,
                    "Source reference inspector target was not found.",
                    new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                    {
                        ["source_import_id"] = selectedSource.SourceImportId,
                        ["source_artifact_id"] = "-",
                        ["message_id"] = "-"
                    });
                return;
            }

            CurrentDetail = SourceReferenceInspectorViewModel.From(detail);
            _logAction(
                "source_reference_inspector_loaded",
                _correlationId,
                "Source reference inspector loaded.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["source_import_id"] = detail.SourceImportId,
                    ["source_artifact_id"] = detail.ArtifactReference?.SourceArtifactId ?? "-",
                    ["message_id"] = detail.MessageReference?.MessageId ?? "-"
                });
        }
        catch (Exception exception)
        {
            if (requestVersion != _inspectorLoadVersion || !ReferenceEquals(SelectedSource, selectedSource))
            {
                return;
            }

            CurrentDetail = SourceReferenceInspectorViewModel.CreateLoadFailure();
            _logAction(
                "source_reference_inspector_load_failed",
                _correlationId,
                "Source reference inspector load failed.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["source_import_id"] = selectedSource.SourceImportId,
                    ["source_artifact_id"] = "-",
                    ["message_id"] = "-",
                    ["failure_type"] = exception.GetType().Name
                });
        }
    }

    private IReadOnlyDictionary<string, string> CreateBaseFields()
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["active_case_present"] = HasActiveCase.ToString(CultureInfo.InvariantCulture)
        };

        if (_activeCase is not null)
        {
            fields["case_id"] = _activeCase.CaseId;
        }

        return fields;
    }

    private void ReplaceSources(IEnumerable<SourceListItemViewModel> items)
    {
        Sources.Clear();
        foreach (var item in items)
        {
            Sources.Add(item);
        }

        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(SourceCount));
        OnPropertyChanged(nameof(TotalRecordCount));
        OnPropertyChanged(nameof(TotalWarningCount));
    }
}
