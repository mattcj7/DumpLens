using System.Collections.ObjectModel;
using System.ComponentModel;
using DumpLens.Application.Audit;
using DumpLens.Application.CallImports;
using DumpLens.Application.Cases;
using DumpLens.Application.Imports;
using DumpLens.Application.MessageImports;
using DumpLens.Application.Sources;

namespace DumpLens.App.ViewModels;

public sealed class MainShellViewModel : ObservableObject
{
    private static readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> NoOpLogAction = static (_, _, _, _) => { };

    private readonly ICaseService _caseService;
    private readonly ICallImportService _callImportService;
    private readonly Func<string, IAuditLogger>? _auditLoggerFactory;
    private readonly IImportWarningSummaryReader _importWarningSummaryReader;
    private readonly IReadOnlyList<ISourceImporter> _sourceImporters;
    private readonly ISourceManagerService _sourceManagerService;
    private readonly IMessageImportService _messageImportService;
    private readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> _logAction;
    private readonly ISourceRegistrationService _sourceRegistrationService;
    private CreateCaseResult? _activeCase;
    private CaseCreationViewModel? _caseCreation;
    private ImportWizardViewModel? _importWizard;
    private string _globalCaseTitle;
    private string _globalCaseContext;
    private bool _isCaseCreationOpen;
    private bool _isImportWizardOpen;
    private string _shellStatusMessage;
    private NavigationItemViewModel _selectedNavigationItem;
    private WorkspaceViewModelBase _currentWorkspace;
    private InspectorViewModelBase _inspector;
    private SourceManagerViewModel? _sourceManagerWorkspace;

    public MainShellViewModel()
        : this(
            new UnavailableCaseService(),
            Array.Empty<ISourceImporter>(),
            new UnavailableSourceManagerService(),
            new UnavailableSourceRegistrationService(),
            new UnavailableMessageImportService(),
            new UnavailableCallImportService(),
            new EmptyImportWarningSummaryReader(),
            auditLoggerFactory: null,
            logAction: null)
    {
    }

    public MainShellViewModel(
        ICaseService caseService,
        Action<string, string, string, IReadOnlyDictionary<string, string>?>? logAction = null)
        : this(
            caseService,
            Array.Empty<ISourceImporter>(),
            new UnavailableSourceManagerService(),
            new UnavailableSourceRegistrationService(),
            new UnavailableMessageImportService(),
            new UnavailableCallImportService(),
            new EmptyImportWarningSummaryReader(),
            auditLoggerFactory: null,
            logAction)
    {
    }

    public MainShellViewModel(
        ICaseService caseService,
        IEnumerable<ISourceImporter> sourceImporters,
        ISourceManagerService sourceManagerService,
        ISourceRegistrationService sourceRegistrationService,
        IMessageImportService messageImportService,
        ICallImportService callImportService,
        IImportWarningSummaryReader importWarningSummaryReader,
        Func<string, IAuditLogger>? auditLoggerFactory,
        Action<string, string, string, IReadOnlyDictionary<string, string>?>? logAction = null)
    {
        _caseService = caseService ?? throw new ArgumentNullException(nameof(caseService));
        _sourceImporters = sourceImporters?.ToArray() ?? throw new ArgumentNullException(nameof(sourceImporters));
        _sourceManagerService = sourceManagerService ?? throw new ArgumentNullException(nameof(sourceManagerService));
        _sourceRegistrationService = sourceRegistrationService ?? throw new ArgumentNullException(nameof(sourceRegistrationService));
        _messageImportService = messageImportService ?? throw new ArgumentNullException(nameof(messageImportService));
        _callImportService = callImportService ?? throw new ArgumentNullException(nameof(callImportService));
        _importWarningSummaryReader = importWarningSummaryReader ?? throw new ArgumentNullException(nameof(importWarningSummaryReader));
        _auditLoggerFactory = auditLoggerFactory;
        _logAction = logAction ?? NoOpLogAction;
        _globalCaseTitle = "No case selected";
        _globalCaseContext = "Create a case package to start working in DumpLens.";
        _shellStatusMessage = "No case package has been created in this shell yet.";
        NavigationItems = new ObservableCollection<NavigationItemViewModel>(CreateNavigationItems());
        _selectedNavigationItem = NavigationItems[0];
        _currentWorkspace = CreatePlaceholderWorkspace(_selectedNavigationItem);
        _inspector = CreatePlaceholderInspector(_selectedNavigationItem);
        OpenCaseCreationCommand = new RelayCommand(OpenCaseCreation);
        OpenImportWizardCommand = new RelayCommand(OpenImportWizard);
    }

    public CaseCreationViewModel? CaseCreation
    {
        get => _caseCreation;
        private set => SetProperty(ref _caseCreation, value);
    }

    public WorkspaceViewModelBase CurrentWorkspace
    {
        get => _currentWorkspace;
        private set => SetProperty(ref _currentWorkspace, value);
    }

    public string GlobalCaseContext
    {
        get => _globalCaseContext;
        private set => SetProperty(ref _globalCaseContext, value);
    }

    public string GlobalCaseTitle
    {
        get => _globalCaseTitle;
        private set => SetProperty(ref _globalCaseTitle, value);
    }

    public ImportWizardViewModel? ImportWizard
    {
        get => _importWizard;
        private set => SetProperty(ref _importWizard, value);
    }

    public InspectorViewModelBase Inspector
    {
        get => _inspector;
        private set => SetProperty(ref _inspector, value);
    }

    public bool IsCaseCreationOpen
    {
        get => _isCaseCreationOpen;
        private set => SetProperty(ref _isCaseCreationOpen, value);
    }

    public bool IsImportWizardOpen
    {
        get => _isImportWizardOpen;
        private set => SetProperty(ref _isImportWizardOpen, value);
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public RelayCommand OpenCaseCreationCommand { get; }

    public RelayCommand OpenImportWizardCommand { get; }

    public NavigationItemViewModel SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (value is null)
            {
                return;
            }

            if (!SetProperty(ref _selectedNavigationItem, value))
            {
                return;
            }

            RefreshSurfaceState();
        }
    }

    public string ShellStatusMessage
    {
        get => _shellStatusMessage;
        private set => SetProperty(ref _shellStatusMessage, value);
    }

    private PlaceholderWorkspaceViewModel CreatePlaceholderWorkspace(NavigationItemViewModel navigationItem)
    {
        if (string.Equals(navigationItem.Label, "Dashboard", StringComparison.Ordinal) &&
            !string.Equals(GlobalCaseTitle, "No case selected", StringComparison.Ordinal))
        {
            return new PlaceholderWorkspaceViewModel(
                title: navigationItem.Label,
                description: "Case package created successfully. This dashboard remains a safe placeholder until later tickets add live case metrics and review queues.",
                bodyText: ShellStatusMessage,
                nextStepText: "Later tickets can add case health, recent activity, and review queue summaries without changing the shell layout.");
        }

        return new PlaceholderWorkspaceViewModel(
            title: navigationItem.Label,
            description: navigationItem.WorkspaceDescription,
            bodyText: navigationItem.WorkspaceBodyText,
            nextStepText: navigationItem.WorkspaceNextStepText);
    }

    private InspectorPlaceholderViewModel CreatePlaceholderInspector(NavigationItemViewModel navigationItem)
    {
        if (string.Equals(navigationItem.Label, "Dashboard", StringComparison.Ordinal) &&
            !string.Equals(GlobalCaseTitle, "No case selected", StringComparison.Ordinal))
        {
            return new InspectorPlaceholderViewModel(
                title: "Case Summary",
                description: GlobalCaseTitle,
                bodyText: GlobalCaseContext);
        }

        return new InspectorPlaceholderViewModel(
            title: "Selection and Source Reference",
            description: navigationItem.InspectorDescription,
            bodyText: navigationItem.InspectorBodyText);
    }

    private void OpenCaseCreation()
    {
        CloseImportWizard();
        CaseCreation = new CaseCreationViewModel(
            _caseService,
            OnCaseCreated,
            CloseCaseCreation,
            _logAction);
        IsCaseCreationOpen = true;
    }

    private void CloseCaseCreation()
    {
        IsCaseCreationOpen = false;
        CaseCreation = null;
    }

    private void OpenImportWizard()
    {
        CloseCaseCreation();
        ImportWizard = new ImportWizardViewModel(
            _sourceImporters,
            _activeCase,
            _sourceRegistrationService,
            _messageImportService,
            _callImportService,
            _importWarningSummaryReader,
            _auditLoggerFactory,
            CloseImportWizard,
            _logAction);
        IsImportWizardOpen = true;
    }

    private void CloseImportWizard()
    {
        IsImportWizardOpen = false;
        ImportWizard = null;
    }

    private void OnCaseCreated(CreateCaseResult result)
    {
        _activeCase = result;
        GlobalCaseTitle = result.Title;
        GlobalCaseContext = string.IsNullOrWhiteSpace(result.CaseNumber)
            ? "Case number not provided."
            : $"Case number {result.CaseNumber}";
        ShellStatusMessage = string.IsNullOrWhiteSpace(result.CaseNumber)
            ? $"Created case \"{result.Title}\"."
            : $"Created case \"{result.Title}\" ({result.CaseNumber}).";

        CloseCaseCreation();

        var dashboardItem = NavigationItems[0];
        if (ReferenceEquals(SelectedNavigationItem, dashboardItem))
        {
            RefreshSurfaceState();
        }
        else
        {
            SelectedNavigationItem = dashboardItem;
        }
    }

    private void RefreshSurfaceState()
    {
        DetachSourceManagerWorkspace();

        if (string.Equals(_selectedNavigationItem.Label, "Sources", StringComparison.Ordinal))
        {
            _sourceManagerWorkspace = new SourceManagerViewModel(_activeCase, _sourceManagerService, _logAction);
            _sourceManagerWorkspace.PropertyChanged += OnSourceManagerWorkspacePropertyChanged;
            CurrentWorkspace = _sourceManagerWorkspace;
            Inspector = _sourceManagerWorkspace.CurrentDetail;
            return;
        }

        CurrentWorkspace = CreatePlaceholderWorkspace(_selectedNavigationItem);
        Inspector = CreatePlaceholderInspector(_selectedNavigationItem);
    }

    private void DetachSourceManagerWorkspace()
    {
        if (_sourceManagerWorkspace is null)
        {
            return;
        }

        _sourceManagerWorkspace.PropertyChanged -= OnSourceManagerWorkspacePropertyChanged;
        _sourceManagerWorkspace = null;
    }

    private void OnSourceManagerWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _sourceManagerWorkspace) ||
            !string.Equals(e.PropertyName, nameof(SourceManagerViewModel.CurrentDetail), StringComparison.Ordinal))
        {
            return;
        }

        Inspector = _sourceManagerWorkspace!.CurrentDetail;
    }

    private static IReadOnlyList<NavigationItemViewModel> CreateNavigationItems()
    {
        return
        [
            CreateItem(
                label: "Dashboard",
                summary: "Case overview and next review steps.",
                description: "Start here for a plain-language overview of the case, import status, and review queues once those features are wired.",
                bodyText: "This placeholder keeps the dashboard slot visible without inventing metrics or case data.",
                nextStepText: "Later tickets can add case health, recent activity, and review queue summaries.",
                inspectorDescription: "The right panel will hold selected dashboard detail, source-backed highlights, and review shortcuts.",
                inspectorBodyText: "No dashboard item is selected yet because this shell uses placeholder content only."),
            CreateItem(
                label: "Sources",
                summary: "Imported evidence sources and status.",
                description: "Review registered sources, safe file metadata, import status, counts, and warning summaries for the active case.",
                bodyText: "Select Sources to review imported evidence sources, safe hashes, counts, and warning summaries.",
                nextStepText: "Later tickets can add richer source-reference inspection, conversation links, and review controls without changing this shell layout.",
                inspectorDescription: "The right panel will show source details, safe identifiers, and locator information for the selected source.",
                inspectorBodyText: "Select a source to inspect safe source metadata, hash details, and warning summaries."),
            CreateItem(
                label: "Conversations",
                summary: "Conversation review workspace.",
                description: "Use this workspace to review reconstructed conversations in plain language once conversation building is implemented.",
                bodyText: "This placeholder keeps the conversation review surface in the shell without loading any message data.",
                nextStepText: "Later tickets can add threaded review, source comparison, and review-state controls.",
                inspectorDescription: "The right panel will show the selected item, source support, and review notes for the active conversation entry.",
                inspectorBodyText: "Conversation detail and source references are not wired yet."),
            CreateItem(
                label: "Timeline",
                summary: "Chronological event review.",
                description: "Review source-backed events in time order here once timeline assembly and filtering are available.",
                bodyText: "This placeholder protects the timeline slot and its review-first wording without drawing unsupported conclusions.",
                nextStepText: "Later tickets can add event markers, timeline filters, and source-backed inspection.",
                inspectorDescription: "The right panel will show the selected event, source support, and timing context.",
                inspectorBodyText: "Timeline selection details are not available in this placeholder shell."),
            CreateItem(
                label: "Gaps & Deletions",
                summary: "Possible gaps that need review.",
                description: "This workspace will surface possible missing-message and deletion-gap items using careful language after reconciliation is implemented.",
                bodyText: "The placeholder uses neutral wording only. It does not claim any deletion, tampering, or intent.",
                nextStepText: "Later tickets can add gap queues, alternative explanations, and source comparison context.",
                inspectorDescription: "The right panel will show review status, source support, and alternative explanations for the selected gap item.",
                inspectorBodyText: "No gap candidate is selected because reconciliation is out of scope for this ticket."),
            CreateItem(
                label: "Entities & Aliases",
                summary: "People, accounts, and aliases.",
                description: "Review linked people, devices, accounts, and aliases here once identity handling is implemented.",
                bodyText: "This placeholder reserves the workspace for entity review without creating any matching or merge behavior.",
                nextStepText: "Later tickets can add entity cards, alias management, and traceability back to supporting artifacts.",
                inspectorDescription: "The right panel will show the selected entity, linked aliases, and supporting sources.",
                inspectorBodyText: "Entity detail is intentionally absent in the initial shell."),
            CreateItem(
                label: "Leads",
                summary: "Suggested investigative follow-up.",
                description: "Use this workspace to review investigator-created or system-suggested leads once those workflows exist.",
                bodyText: "This placeholder keeps the lead review area visible without generating any lead content.",
                nextStepText: "Later tickets can add lead status, supporting citations, and review actions.",
                inspectorDescription: "The right panel will show lead status, supporting sources, and next-step notes.",
                inspectorBodyText: "No leads are available because lead creation is out of scope for this ticket."),
            CreateItem(
                label: "AI Findings",
                summary: "AI-assisted items pending review.",
                description: "This workspace will hold AI-assisted findings with citations and review controls after the AI layer exists.",
                bodyText: "The placeholder maintains the shell location while preserving the rule that AI is optional and review-only.",
                nextStepText: "Later tickets can add AI-assisted summaries, citation views, and explicit approval or rejection states.",
                inspectorDescription: "The right panel will show AI provenance, supporting sources, and human review state.",
                inspectorBodyText: "No AI-assisted content is connected in the initial shell."),
            CreateItem(
                label: "Reports",
                summary: "Report drafting and export.",
                description: "Draft and review source-cited reports here after reporting workflows are implemented.",
                bodyText: "This placeholder reserves the reporting workspace without exposing export, formatting, or final findings behavior.",
                nextStepText: "Later tickets can add report drafts, cited evidence lists, and export status.",
                inspectorDescription: "The right panel will show report metadata, included citations, and review status for the selected section.",
                inspectorBodyText: "Report detail is not connected in this ticket."),
            CreateItem(
                label: "Settings",
                summary: "Application and case settings.",
                description: "Review app-wide and case-level settings here once those settings exist.",
                bodyText: "This placeholder keeps settings discoverable without adding any persistence or configuration behavior.",
                nextStepText: "Later tickets can add storage, AI mode, logging, and review preference settings.",
                inspectorDescription: "The right panel will show the selected setting, its current value, and the effect of changing it.",
                inspectorBodyText: "Settings are intentionally disabled in the initial shell.")
        ];
    }

    private static NavigationItemViewModel CreateItem(
        string label,
        string summary,
        string description,
        string bodyText,
        string nextStepText,
        string inspectorDescription,
        string inspectorBodyText)
    {
        return new NavigationItemViewModel(
            label,
            summary,
            description,
            bodyText,
            nextStepText,
            inspectorDescription,
            inspectorBodyText);
    }

    private sealed class UnavailableCaseService : ICaseService
    {
        public Task<CreateCaseResult> CreateAsync(
            CreateCaseRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No case service is configured for this shell instance.");
        }
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

    private sealed class UnavailableSourceManagerService : ISourceManagerService
    {
        public Task<SourceImportDetail?> GetDetailAsync(
            LoadSourceImportDetailRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No source manager service is configured for this shell instance.");
        }

        public Task<IReadOnlyList<SourceImportSummary>> GetSummariesAsync(
            LoadSourceImportSummariesRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No source manager service is configured for this shell instance.");
        }
    }

    private sealed class UnavailableCallImportService : ICallImportService
    {
        public Task<ImportCallsResult> ImportAsync(
            ImportCallsRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No call import service is configured for this shell instance.");
        }
    }

    private sealed class UnavailableMessageImportService : IMessageImportService
    {
        public Task<ImportMessagesResult> ImportAsync(
            ImportMessagesRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No message import service is configured for this shell instance.");
        }
    }

    private sealed class UnavailableSourceRegistrationService : ISourceRegistrationService
    {
        public Task<RegisterSourceResult> RegisterAsync(
            RegisterSourceRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No source registration service is configured for this shell instance.");
        }
    }
}
