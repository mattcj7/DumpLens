using System.Collections.ObjectModel;
using DumpLens.Application.Cases;

namespace DumpLens.App.ViewModels;

public sealed class MainShellViewModel : ObservableObject
{
    private static readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> NoOpLogAction = static (_, _, _, _) => { };

    private readonly ICaseService _caseService;
    private readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> _logAction;
    private CaseCreationViewModel? _caseCreation;
    private string _globalCaseTitle;
    private string _globalCaseContext;
    private bool _isCaseCreationOpen;
    private string _shellStatusMessage;
    private NavigationItemViewModel _selectedNavigationItem;
    private PlaceholderWorkspaceViewModel _currentWorkspace;
    private InspectorPlaceholderViewModel _inspector;

    public MainShellViewModel()
        : this(new UnavailableCaseService(), null)
    {
    }

    public MainShellViewModel(
        ICaseService caseService,
        Action<string, string, string, IReadOnlyDictionary<string, string>?>? logAction = null)
    {
        _caseService = caseService ?? throw new ArgumentNullException(nameof(caseService));
        _logAction = logAction ?? NoOpLogAction;
        _globalCaseTitle = "No case selected";
        _globalCaseContext = "Create a case package to start working in DumpLens.";
        _shellStatusMessage = "No case package has been created in this shell yet.";
        NavigationItems = new ObservableCollection<NavigationItemViewModel>(CreateNavigationItems());
        _selectedNavigationItem = NavigationItems[0];
        _currentWorkspace = CreateWorkspace(_selectedNavigationItem);
        _inspector = CreateInspector(_selectedNavigationItem);
        OpenCaseCreationCommand = new RelayCommand(OpenCaseCreation);
    }

    public CaseCreationViewModel? CaseCreation
    {
        get => _caseCreation;
        private set => SetProperty(ref _caseCreation, value);
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

    public bool IsCaseCreationOpen
    {
        get => _isCaseCreationOpen;
        private set => SetProperty(ref _isCaseCreationOpen, value);
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public RelayCommand OpenCaseCreationCommand { get; }

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

    public PlaceholderWorkspaceViewModel CurrentWorkspace
    {
        get => _currentWorkspace;
        private set => SetProperty(ref _currentWorkspace, value);
    }

    public InspectorPlaceholderViewModel Inspector
    {
        get => _inspector;
        private set => SetProperty(ref _inspector, value);
    }

    public string ShellStatusMessage
    {
        get => _shellStatusMessage;
        private set => SetProperty(ref _shellStatusMessage, value);
    }

    private PlaceholderWorkspaceViewModel CreateWorkspace(NavigationItemViewModel navigationItem)
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

    private InspectorPlaceholderViewModel CreateInspector(NavigationItemViewModel navigationItem)
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
    }

    private void OnCaseCreated(CreateCaseResult result)
    {
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
        CurrentWorkspace = CreateWorkspace(_selectedNavigationItem);
        Inspector = CreateInspector(_selectedNavigationItem);
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
                description: "Review imported evidence sources, source types, and intake status here once the source manager is implemented.",
                bodyText: "This placeholder reserves the workspace for source intake, source registration, and evidence status.",
                nextStepText: "A later ticket can add source cards, import progress, and one-click traceability back to the original artifact.",
                inspectorDescription: "The right panel will show source details, safe identifiers, and locator information for the selected source.",
                inspectorBodyText: "Source reference details will appear here after the source manager exists."),
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
}
