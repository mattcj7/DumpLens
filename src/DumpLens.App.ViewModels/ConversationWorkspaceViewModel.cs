using System.Collections.ObjectModel;
using System.Globalization;
using DumpLens.Application.Cases;
using DumpLens.Application.Conversations;
using DumpLens.Application.SourceReferences;

namespace DumpLens.App.ViewModels;

public sealed class ConversationWorkspaceViewModel : WorkspaceViewModelBase
{
    private const string ActiveCaseMissingOperation = "conversation_workspace_active_case_missing";
    private const string ConversationListLoadFailedOperation = "conversation_workspace_conversation_list_load_failed";
    private const string ConversationListLoadRequestedOperation = "conversation_workspace_conversation_list_load_requested";
    private const string ConversationListLoadSucceededOperation = "conversation_workspace_conversation_list_load_succeeded";
    private const string ConversationSelectedOperation = "conversation_workspace_conversation_selected";
    private const string MessageSelectedOperation = "conversation_workspace_message_selected";
    private const string ThreadLoadFailedOperation = "conversation_workspace_thread_load_failed";
    private const string ThreadLoadRequestedOperation = "conversation_workspace_thread_load_requested";
    private const string ThreadLoadSucceededOperation = "conversation_workspace_thread_load_succeeded";
    private const string WorkspaceOpenedOperation = "conversation_workspace_opened";
    private static readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> NoOpLogAction = static (_, _, _, _) => { };

    private readonly CreateCaseResult? _activeCase;
    private readonly string _correlationId;
    private readonly IConversationReader _conversationReader;
    private readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> _logAction;
    private readonly ISourceReferenceReader _sourceReferenceReader;
    private ConversationListItemViewModel? _selectedConversation;
    private ConversationThreadMessageViewModel? _selectedMessage;
    private SourceReferenceInspectorViewModel _currentInspector;
    private string? _conversationListErrorMessage;
    private bool _isConversationListLoading;
    private int _inspectorLoadVersion;
    private bool _isThreadLoading;
    private string _statusMessage;
    private string _threadErrorMessage;
    private string _threadStatusMessage;
    private int _threadLoadVersion;

    public ConversationWorkspaceViewModel(
        CreateCaseResult? activeCase,
        IConversationReader conversationReader,
        ISourceReferenceReader sourceReferenceReader,
        Action<string, string, string, IReadOnlyDictionary<string, string>?>? logAction = null)
        : base(
            "Conversations",
            "Review reconstructed conversations, message threads, and safe source context for the active case.")
    {
        _activeCase = activeCase;
        _conversationReader = conversationReader ?? throw new ArgumentNullException(nameof(conversationReader));
        _sourceReferenceReader = sourceReferenceReader ?? throw new ArgumentNullException(nameof(sourceReferenceReader));
        _logAction = logAction ?? NoOpLogAction;
        _correlationId = Guid.NewGuid().ToString("N");
        _statusMessage = activeCase is null
            ? "Create or open a case to view conversations."
            : "Loading conversations for the active case.";
        _threadStatusMessage = activeCase is null
            ? "Create or open a case to view conversations."
            : "Select a conversation to view its message thread.";
        _threadErrorMessage = string.Empty;
        _currentInspector = activeCase is null
            ? SourceReferenceInspectorViewModel.CreateActiveCaseMissing()
            : SourceReferenceInspectorViewModel.CreateNoSelection();

        Conversations = new ObservableCollection<ConversationListItemViewModel>();
        ThreadMessages = new ObservableCollection<ConversationThreadMessageViewModel>();

        _logAction(
            WorkspaceOpenedOperation,
            _correlationId,
            "Conversation workspace opened.",
            CreateBaseFields());

        if (_activeCase is null)
        {
            _logAction(
                ActiveCaseMissingOperation,
                _correlationId,
                "Conversation workspace requires an active case.",
                CreateBaseFields());
            return;
        }

        _ = LoadConversationSummariesAsync();
    }

    public ObservableCollection<ConversationListItemViewModel> Conversations { get; }

    public string ConversationListEmptyStateMessage => HasActiveCase
        ? "No conversations have been built for this case yet. Run the conversation builder after importing messages."
        : "Create or open a case to view conversations.";

    public string? ConversationListErrorMessage
    {
        get => _conversationListErrorMessage;
        private set
        {
            if (SetProperty(ref _conversationListErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasConversationListError));
                OnPropertyChanged(nameof(IsConversationListEmptyStateVisible));
            }
        }
    }

    public int ConversationCount => Conversations.Count;

    public SourceReferenceInspectorViewModel CurrentInspector
    {
        get => _currentInspector;
        private set => SetProperty(ref _currentInspector, value);
    }

    public bool HasActiveCase => _activeCase is not null;

    public bool HasConversationListError => !string.IsNullOrWhiteSpace(ConversationListErrorMessage);

    public bool HasConversations => Conversations.Count > 0;

    public bool HasSelectedConversation => SelectedConversation is not null;

    public bool HasThreadError => !string.IsNullOrWhiteSpace(ThreadErrorMessage);

    public bool HasThreadMessages => ThreadMessages.Count > 0;

    public bool IsConversationListEmptyStateVisible => !IsConversationListLoading && !HasConversationListError && !HasConversations;

    public bool IsConversationListLoading
    {
        get => _isConversationListLoading;
        private set
        {
            if (SetProperty(ref _isConversationListLoading, value))
            {
                OnPropertyChanged(nameof(IsConversationListEmptyStateVisible));
            }
        }
    }

    public bool IsThreadEmptyStateVisible => !IsThreadLoading && !HasThreadError && !HasThreadMessages;

    public bool IsThreadLoading
    {
        get => _isThreadLoading;
        private set
        {
            if (SetProperty(ref _isThreadLoading, value))
            {
                OnPropertyChanged(nameof(IsThreadEmptyStateVisible));
            }
        }
    }

    public ConversationListItemViewModel? SelectedConversation
    {
        get => _selectedConversation;
        set
        {
            if (!SetProperty(ref _selectedConversation, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedConversation));
            SelectedMessage = null;
            CurrentInspector = HasActiveCase
                ? SourceReferenceInspectorViewModel.CreateNoSelection()
                : SourceReferenceInspectorViewModel.CreateActiveCaseMissing();
            _ = LoadThreadAsync(value);
        }
    }

    public ConversationThreadMessageViewModel? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            if (!SetProperty(ref _selectedMessage, value))
            {
                return;
            }

            if (value is null)
            {
                CurrentInspector = HasActiveCase
                    ? SourceReferenceInspectorViewModel.CreateNoSelection()
                    : SourceReferenceInspectorViewModel.CreateActiveCaseMissing();
                return;
            }

            CurrentInspector = SourceReferenceInspectorViewModel.CreateLoading();

            _logAction(
                MessageSelectedOperation,
                _correlationId,
                "Message selected.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["conversation_id"] = SelectedConversation?.ConversationId ?? "-",
                    ["message_id"] = value.MessageId
                });

            _ = LoadSourceReferenceInspectorAsync(value);
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int ThreadMessageCount => ThreadMessages.Count;

    public ObservableCollection<ConversationThreadMessageViewModel> ThreadMessages { get; }

    public string ThreadEmptyStateMessage => HasActiveCase
        ? SelectedConversation is null
            ? "Select a conversation to view its message thread."
            : "No messages are assigned to the selected conversation."
        : "Create or open a case to view conversations.";

    public string ThreadErrorMessage
    {
        get => _threadErrorMessage;
        private set
        {
            if (SetProperty(ref _threadErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasThreadError));
                OnPropertyChanged(nameof(IsThreadEmptyStateVisible));
            }
        }
    }

    public string ThreadStatusMessage
    {
        get => _threadStatusMessage;
        private set => SetProperty(ref _threadStatusMessage, value);
    }

    private async Task LoadConversationSummariesAsync()
    {
        if (_activeCase is null)
        {
            return;
        }

        IsConversationListLoading = true;
        ConversationListErrorMessage = null;
        StatusMessage = "Loading conversations for the active case.";
        ReplaceThreadMessages([]);
        ThreadErrorMessage = string.Empty;
        ThreadStatusMessage = "Select a conversation to view its message thread.";
        CurrentInspector = SourceReferenceInspectorViewModel.CreateNoSelection();

        _logAction(
            ConversationListLoadRequestedOperation,
            _correlationId,
            "Conversation list load requested.",
            CreateBaseFields());

        try
        {
            var summaries = await _conversationReader.GetSummariesAsync(
                    new LoadConversationSummariesRequest
                    {
                        CaseId = _activeCase.CaseId,
                        CaseDatabasePath = _activeCase.DatabasePath
                    })
                .ConfigureAwait(false);

            ReplaceConversations(summaries.Select(static summary => new ConversationListItemViewModel(summary)));
            ConversationListErrorMessage = null;
            StatusMessage = summaries.Count == 0
                ? "No conversations have been built for this case yet. Run the conversation builder after importing messages."
                : $"{summaries.Count.ToString(CultureInfo.InvariantCulture)} conversations loaded.";

            _logAction(
                ConversationListLoadSucceededOperation,
                _correlationId,
                "Conversation list load succeeded.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["conversation_count"] = summaries.Count.ToString(CultureInfo.InvariantCulture)
                });

            SelectedConversation = Conversations.FirstOrDefault();
        }
        catch (Exception exception)
        {
            ReplaceConversations([]);
            ReplaceThreadMessages([]);
            SelectedConversation = null;
            ConversationListErrorMessage = "Conversations could not be loaded. Check the case package and try again.";
            StatusMessage = ConversationListErrorMessage;
            ThreadErrorMessage = "Conversation thread is unavailable because the conversation list could not be loaded.";
            ThreadStatusMessage = "Conversation thread is unavailable because the conversation list could not be loaded.";
            CurrentInspector = SourceReferenceInspectorViewModel.CreateLoadFailure();

            _logAction(
                ConversationListLoadFailedOperation,
                _correlationId,
                "Conversation list load failed.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["failure_type"] = exception.GetType().Name
                });
        }
        finally
        {
            IsConversationListLoading = false;
        }
    }

    private async Task LoadThreadAsync(ConversationListItemViewModel? conversation)
    {
        var requestVersion = Interlocked.Increment(ref _threadLoadVersion);

        ReplaceThreadMessages([]);
        ThreadErrorMessage = string.Empty;

        if (_activeCase is null)
        {
            ThreadStatusMessage = "Create or open a case to view conversations.";
            CurrentInspector = SourceReferenceInspectorViewModel.CreateActiveCaseMissing();
            return;
        }

        if (conversation is null)
        {
            ThreadStatusMessage = HasConversations
                ? "Select a conversation to view its message thread."
                : "No conversations have been built for this case yet. Run the conversation builder after importing messages.";
            CurrentInspector = SourceReferenceInspectorViewModel.CreateNoSelection();
            return;
        }

        CurrentInspector = SourceReferenceInspectorViewModel.CreateNoSelection();
        ThreadStatusMessage = "Loading messages for the selected conversation.";
        IsThreadLoading = true;

        _logAction(
            ConversationSelectedOperation,
            _correlationId,
            "Conversation selected.",
            new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
            {
                ["conversation_id"] = conversation.ConversationId
            });

        _logAction(
            ThreadLoadRequestedOperation,
            _correlationId,
            "Thread load requested.",
            new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
            {
                ["conversation_id"] = conversation.ConversationId
            });

        try
        {
            var thread = await _conversationReader.GetThreadAsync(
                    new LoadConversationThreadRequest
                    {
                        CaseId = _activeCase.CaseId,
                        CaseDatabasePath = _activeCase.DatabasePath,
                        ConversationId = conversation.ConversationId
                    })
                .ConfigureAwait(false);

            if (requestVersion != _threadLoadVersion)
            {
                return;
            }

            if (thread is null)
            {
                ReplaceThreadMessages([]);
                ThreadErrorMessage = "Conversation thread could not be loaded. Check the case package and try again.";
                ThreadStatusMessage = ThreadErrorMessage;
                CurrentInspector = SourceReferenceInspectorViewModel.CreateLoadFailure();

                _logAction(
                    ThreadLoadFailedOperation,
                    _correlationId,
                    "Thread load failed.",
                    new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                    {
                        ["conversation_id"] = conversation.ConversationId,
                        ["failure_type"] = "ConversationNotFound"
                    });
                return;
            }

            ReplaceThreadMessages(thread.Messages.Select(static message => new ConversationThreadMessageViewModel(message)));
            ThreadErrorMessage = string.Empty;
            ThreadStatusMessage = thread.Messages.Count == 0
                ? "No messages are assigned to the selected conversation."
                : $"{thread.Messages.Count.ToString(CultureInfo.InvariantCulture)} messages loaded.";
            CurrentInspector = SourceReferenceInspectorViewModel.CreateNoSelection();

            _logAction(
                ThreadLoadSucceededOperation,
                _correlationId,
                "Thread load succeeded.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["conversation_id"] = conversation.ConversationId,
                    ["message_count"] = thread.Messages.Count.ToString(CultureInfo.InvariantCulture)
                });
        }
        catch (Exception exception)
        {
            if (requestVersion != _threadLoadVersion)
            {
                return;
            }

            ReplaceThreadMessages([]);
            ThreadErrorMessage = "Conversation thread could not be loaded. Check the case package and try again.";
            ThreadStatusMessage = ThreadErrorMessage;
            CurrentInspector = SourceReferenceInspectorViewModel.CreateLoadFailure();

            _logAction(
                ThreadLoadFailedOperation,
                _correlationId,
                "Thread load failed.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["conversation_id"] = conversation.ConversationId,
                    ["failure_type"] = exception.GetType().Name
                });
        }
        finally
        {
            if (requestVersion == _threadLoadVersion)
            {
                IsThreadLoading = false;
            }
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

    private void ReplaceConversations(IEnumerable<ConversationListItemViewModel> items)
    {
        Conversations.Clear();
        foreach (var item in items)
        {
            Conversations.Add(item);
        }

        OnPropertyChanged(nameof(ConversationCount));
        OnPropertyChanged(nameof(HasConversations));
        OnPropertyChanged(nameof(IsConversationListEmptyStateVisible));
    }

    private void ReplaceThreadMessages(IEnumerable<ConversationThreadMessageViewModel> items)
    {
        ThreadMessages.Clear();
        foreach (var item in items)
        {
            ThreadMessages.Add(item);
        }

        OnPropertyChanged(nameof(HasThreadMessages));
        OnPropertyChanged(nameof(IsThreadEmptyStateVisible));
        OnPropertyChanged(nameof(ThreadMessageCount));
    }

    private async Task LoadSourceReferenceInspectorAsync(ConversationThreadMessageViewModel message)
    {
        if (_activeCase is null || SelectedConversation is null)
        {
            CurrentInspector = SourceReferenceInspectorViewModel.CreateActiveCaseMissing();
            return;
        }

        var requestVersion = Interlocked.Increment(ref _inspectorLoadVersion);
        var sourceImportId = message.SourceContext?.SourceImportId;

        if (string.IsNullOrWhiteSpace(sourceImportId))
        {
            CurrentInspector = SourceReferenceInspectorViewModel.CreateLoadFailure();
            _logAction(
                "source_reference_inspector_missing",
                _correlationId,
                "Source reference inspector target was not found.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["conversation_id"] = SelectedConversation.ConversationId,
                    ["message_id"] = message.MessageId,
                    ["source_import_id"] = "-",
                    ["source_artifact_id"] = message.SourceContext?.SourceArtifactId ?? "-"
                });
            return;
        }

        _logAction(
            "source_reference_inspector_requested",
            _correlationId,
            "Source reference inspector requested.",
            new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
            {
                ["conversation_id"] = SelectedConversation.ConversationId,
                ["message_id"] = message.MessageId,
                ["source_import_id"] = sourceImportId,
                ["source_artifact_id"] = message.SourceContext?.SourceArtifactId ?? "-"
            });

        try
        {
            var detail = await _sourceReferenceReader.LoadAsync(
                    new LoadSourceReferenceRequest
                    {
                        CaseId = _activeCase.CaseId,
                        CaseDatabasePath = _activeCase.DatabasePath,
                        CasePackageRootPath = _activeCase.PackageRootPath,
                        SourceImportId = sourceImportId,
                        SourceArtifactId = message.SourceContext?.SourceArtifactId,
                        MessageId = message.MessageId,
                        CorrelationId = _correlationId
                    })
                .ConfigureAwait(false);

            if (requestVersion != _inspectorLoadVersion || !ReferenceEquals(SelectedMessage, message))
            {
                return;
            }

            if (detail is null)
            {
                CurrentInspector = SourceReferenceInspectorViewModel.CreateLoadFailure();
                _logAction(
                    "source_reference_inspector_missing",
                    _correlationId,
                    "Source reference inspector target was not found.",
                    new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                    {
                        ["conversation_id"] = SelectedConversation.ConversationId,
                        ["message_id"] = message.MessageId,
                        ["source_import_id"] = sourceImportId,
                        ["source_artifact_id"] = message.SourceContext?.SourceArtifactId ?? "-"
                    });
                return;
            }

            CurrentInspector = SourceReferenceInspectorViewModel.From(detail);
            _logAction(
                "source_reference_inspector_loaded",
                _correlationId,
                "Source reference inspector loaded.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["conversation_id"] = SelectedConversation.ConversationId,
                    ["message_id"] = detail.MessageReference?.MessageId ?? message.MessageId,
                    ["source_import_id"] = detail.SourceImportId,
                    ["source_artifact_id"] = detail.ArtifactReference?.SourceArtifactId ?? message.SourceContext?.SourceArtifactId ?? "-"
                });
        }
        catch (Exception exception)
        {
            if (requestVersion != _inspectorLoadVersion || !ReferenceEquals(SelectedMessage, message))
            {
                return;
            }

            CurrentInspector = SourceReferenceInspectorViewModel.CreateLoadFailure();
            _logAction(
                "source_reference_inspector_load_failed",
                _correlationId,
                "Source reference inspector load failed.",
                new Dictionary<string, string>(CreateBaseFields(), StringComparer.Ordinal)
                {
                    ["conversation_id"] = SelectedConversation.ConversationId,
                    ["message_id"] = message.MessageId,
                    ["source_import_id"] = sourceImportId,
                    ["source_artifact_id"] = message.SourceContext?.SourceArtifactId ?? "-",
                    ["failure_type"] = exception.GetType().Name
                });
        }
    }
}
