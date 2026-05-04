using System.Collections.ObjectModel;
using System.Globalization;
using DumpLens.Application.Cases;
using DumpLens.Application.Search;

namespace DumpLens.App.ViewModels;

public sealed class SearchWorkspaceViewModel : WorkspaceViewModelBase
{
    private const string ActiveCaseMissingOperation = "search_workspace_active_case_missing";
    private const string RebuildFailedOperation = "search_workspace_rebuild_failed";
    private const string RebuildRequestedOperation = "search_workspace_rebuild_requested";
    private const string RebuildSucceededOperation = "search_workspace_rebuild_succeeded";
    private const string ResultSelectedOperation = "search_workspace_result_selected";
    private const string SearchFailedOperation = "search_workspace_search_failed";
    private const string SearchRequestedOperation = "search_workspace_search_requested";
    private const string SearchSucceededOperation = "search_workspace_search_succeeded";
    private const string WorkspaceOpenedOperation = "search_workspace_opened";
    private static readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> NoOpLogAction = static (_, _, _, _) => { };

    private readonly CreateCaseResult? _activeCase;
    private readonly string _correlationId;
    private readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> _logAction;
    private readonly IMessageSearchIndexService _messageSearchIndexService;
    private SearchInspectorViewModel _currentInspector;
    private string? _errorMessage;
    private bool _hasSearched;
    private bool _isBusy;
    private SearchResultItemViewModel? _selectedResult;
    private string _searchQueryText;
    private string _statusMessage;
    private string? _validationMessage;

    public SearchWorkspaceViewModel(
        CreateCaseResult? activeCase,
        IMessageSearchIndexService messageSearchIndexService,
        Action<string, string, string, IReadOnlyDictionary<string, string>?>? logAction = null)
        : base(
            "Search",
            "Search messages in the active case and inspect safe source-backed references.")
    {
        _activeCase = activeCase;
        _messageSearchIndexService = messageSearchIndexService ?? throw new ArgumentNullException(nameof(messageSearchIndexService));
        _logAction = logAction ?? NoOpLogAction;
        _correlationId = Guid.NewGuid().ToString("N");
        _searchQueryText = string.Empty;
        _statusMessage = activeCase is null
            ? "Create or open a case to search messages."
            : "Enter one or more search terms to search messages in this case.";
        _currentInspector = activeCase is null
            ? SearchInspectorViewModel.CreateActiveCaseMissing()
            : SearchInspectorViewModel.CreateNoResultSelected();

        Results = new ObservableCollection<SearchResultItemViewModel>();
        SearchCommand = new AsyncRelayCommand(ExecuteSearchAsync, CanSubmitWork);
        RebuildSearchIndexCommand = new AsyncRelayCommand(ExecuteRebuildAsync, CanSubmitWork);

        _logAction(
            WorkspaceOpenedOperation,
            _correlationId,
            "Search workspace opened.",
            CreateBaseFields());

        if (_activeCase is null)
        {
            _logAction(
                ActiveCaseMissingOperation,
                _correlationId,
                "Search workspace requires an active case.",
                CreateBaseFields());
        }
    }

    public bool CanEditSearchQuery => HasActiveCase && !IsBusy;

    public SearchInspectorViewModel CurrentInspector
    {
        get => _currentInspector;
        private set => SetProperty(ref _currentInspector, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(IsResultsEmptyStateVisible));
            }
        }
    }

    public bool HasActiveCase => _activeCase is not null;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasResults => Results.Count > 0;

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanEditSearchQuery));
            OnPropertyChanged(nameof(IsResultsEmptyStateVisible));
            SearchCommand.RaiseCanExecuteChanged();
            RebuildSearchIndexCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsNoActiveCaseVisible => !HasActiveCase;

    public bool IsResultsEmptyStateVisible => HasActiveCase && !IsBusy && !HasError && !HasValidationMessage && !HasResults;

    public AsyncRelayCommand RebuildSearchIndexCommand { get; }

    public ObservableCollection<SearchResultItemViewModel> Results { get; }

    public string ResultsEmptyStateMessage => !_hasSearched
        ? "Enter one or more search terms to search messages in this case."
        : "No matching messages found.";

    public int SearchResultCount => Results.Count;

    public AsyncRelayCommand SearchCommand { get; }

    public string SearchQueryText
    {
        get => _searchQueryText;
        set
        {
            if (!SetProperty(ref _searchQueryText, value))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                ValidationMessage = null;
            }
        }
    }

    public SearchResultItemViewModel? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (!SetProperty(ref _selectedResult, value))
            {
                return;
            }

            if (value is null)
            {
                CurrentInspector = HasActiveCase
                    ? SearchInspectorViewModel.CreateNoResultSelected()
                    : SearchInspectorViewModel.CreateActiveCaseMissing();
                return;
            }

            CurrentInspector = SearchInspectorViewModel.FromResult(value);
            _logAction(
                ResultSelectedOperation,
                _correlationId,
                "Search result selected.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["message_id"] = value.MessageId,
                    ["conversation_id"] = value.ConversationId ?? "-",
                    ["source_import_id"] = value.SourceImportId,
                    ["source_artifact_id"] = value.SourceArtifactId ?? "-"
                });
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationMessage));
                OnPropertyChanged(nameof(IsResultsEmptyStateVisible));
            }
        }
    }

    private bool CanSubmitWork()
    {
        return HasActiveCase && !IsBusy;
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

    private async Task ExecuteRebuildAsync()
    {
        if (_activeCase is null)
        {
            return;
        }

        ValidationMessage = null;
        ErrorMessage = null;
        StatusMessage = "Rebuilding the search index for the active case.";
        IsBusy = true;

        _logAction(
            RebuildRequestedOperation,
            _correlationId,
            "Search index rebuild requested.",
            CreateBaseFields());

        try
        {
            var result = await _messageSearchIndexService.RebuildAsync(
                new RebuildMessageSearchIndexRequest
                {
                    CaseId = _activeCase.CaseId,
                    CaseDatabasePath = _activeCase.DatabasePath,
                    CorrelationId = _correlationId
                });

            StatusMessage = result.IndexedCount == 1
                ? "Search index rebuilt for this case. Indexed 1 message."
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Search index rebuilt for this case. Indexed {0} messages.",
                    result.IndexedCount);

            _logAction(
                RebuildSucceededOperation,
                _correlationId,
                "Search index rebuild succeeded.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["indexed_count"] = result.IndexedCount.ToString(CultureInfo.InvariantCulture)
                });
        }
        catch (Exception exception)
        {
            ErrorMessage = "Search index rebuild could not be completed. Check the case package and try again.";
            StatusMessage = ErrorMessage;

            _logAction(
                RebuildFailedOperation,
                _correlationId,
                "Search index rebuild failed.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["failure_type"] = exception.GetType().Name
                });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteSearchAsync()
    {
        if (_activeCase is null)
        {
            return;
        }

        ValidationMessage = null;
        ErrorMessage = null;

        _logAction(
            SearchRequestedOperation,
            _correlationId,
            "Message search requested.",
            new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
            {
                ["query_present"] = (!string.IsNullOrWhiteSpace(SearchQueryText)).ToString(CultureInfo.InvariantCulture)
            });

        if (string.IsNullOrWhiteSpace(SearchQueryText))
        {
            SetHasSearched(false);
            ReplaceResults([]);
            SelectedResult = null;
            ValidationMessage = "Enter one or more search terms.";
            StatusMessage = ValidationMessage;

            _logAction(
                SearchSucceededOperation,
                _correlationId,
                "Message search completed with validation feedback.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["is_query_valid"] = false.ToString(CultureInfo.InvariantCulture),
                    ["result_count"] = "0",
                    ["validation_error_code"] = MessageSearchValidationCodes.EmptyQuery
                });
            return;
        }

        StatusMessage = "Searching messages in the active case.";
        IsBusy = true;

        try
        {
            var result = await _messageSearchIndexService.SearchAsync(
                new SearchMessagesRequest
                {
                    CaseId = _activeCase.CaseId,
                    CaseDatabasePath = _activeCase.DatabasePath,
                    QueryText = SearchQueryText,
                    CorrelationId = _correlationId
                });

            if (!result.IsQueryValid)
            {
                SetHasSearched(false);
                ReplaceResults([]);
                SelectedResult = null;
                ValidationMessage = string.IsNullOrWhiteSpace(result.ValidationMessage)
                    ? "The search query could not be processed safely."
                    : result.ValidationMessage;
                StatusMessage = ValidationMessage;

                _logAction(
                    SearchSucceededOperation,
                    _correlationId,
                    "Message search completed with validation feedback.",
                    new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                    {
                        ["is_query_valid"] = false.ToString(CultureInfo.InvariantCulture),
                        ["result_count"] = result.ResultCount.ToString(CultureInfo.InvariantCulture),
                        ["validation_error_code"] = result.ValidationErrorCode ?? MessageSearchValidationCodes.UnsupportedQuery
                    });
                return;
            }

            SetHasSearched(true);
            SelectedResult = null;
            ReplaceResults(result.Results.Select(static item => new SearchResultItemViewModel(item)));
            CurrentInspector = SearchInspectorViewModel.CreateNoResultSelected();
            StatusMessage = result.ResultCount == 0
                ? "No matching messages found."
                : result.ResultCount == 1
                    ? "1 matching message found."
                    : string.Format(CultureInfo.InvariantCulture, "{0} matching messages found.", result.ResultCount);

            _logAction(
                SearchSucceededOperation,
                _correlationId,
                "Message search succeeded.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["is_query_valid"] = true.ToString(CultureInfo.InvariantCulture),
                    ["result_count"] = result.ResultCount.ToString(CultureInfo.InvariantCulture)
                });
        }
        catch (Exception exception)
        {
            SetHasSearched(false);
            ReplaceResults([]);
            SelectedResult = null;
            ErrorMessage = "Search could not be completed. Try again or rebuild the search index.";
            StatusMessage = ErrorMessage;
            CurrentInspector = SearchInspectorViewModel.CreateNoResultSelected();

            _logAction(
                SearchFailedOperation,
                _correlationId,
                "Message search failed.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["failure_type"] = exception.GetType().Name
                });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReplaceResults(IEnumerable<SearchResultItemViewModel> items)
    {
        Results.Clear();
        foreach (var item in items)
        {
            Results.Add(item);
        }

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(IsResultsEmptyStateVisible));
        OnPropertyChanged(nameof(SearchResultCount));
    }

    private void SetHasSearched(bool value)
    {
        if (_hasSearched == value)
        {
            return;
        }

        _hasSearched = value;
        OnPropertyChanged(nameof(ResultsEmptyStateMessage));
        OnPropertyChanged(nameof(IsResultsEmptyStateVisible));
    }
}
