using System.Collections;
using System.Reflection;
using DumpLens.Application.Cases;
using DumpLens.Application.Conversations;

namespace DumpLens.Tests.Unit.UI;

public sealed class ConversationWorkspaceViewModelTests
{
    [Fact]
    public void ConversationWorkspaceViewModel_No_Active_Case_Shows_Required_Empty_State_And_Does_Not_Load()
    {
        var logs = new List<UiLogEntry>();
        var reader = new FakeConversationReader();
        var viewModel = CreateViewModel(activeCase: null, reader, logs);

        Assert.False(GetBooleanProperty(viewModel, "HasActiveCase"));
        Assert.True(GetBooleanProperty(viewModel, "IsConversationListEmptyStateVisible"));
        Assert.Equal("Create or open a case to view conversations.", GetStringProperty(viewModel, "ConversationListEmptyStateMessage"));
        Assert.Equal("Create or open a case to view conversations.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.Equal(0, GetIntProperty(viewModel, "ConversationCount"));
        Assert.Equal(0, reader.SummariesCallCount);
        Assert.Equal(0, reader.ThreadCallCount);

        var inspector = GetPropertyValue(viewModel, "CurrentInspector");
        Assert.Equal("Create or open a case to inspect conversation source context.", GetStringProperty(inspector, "Description"));

        Assert.Contains(logs, entry => entry.Operation == "conversation_workspace_opened");
        Assert.Contains(logs, entry => entry.Operation == "conversation_workspace_active_case_missing");
    }

    [Fact]
    public async Task ConversationWorkspaceViewModel_Active_Case_With_No_Conversations_Shows_Expected_Empty_State()
    {
        var logs = new List<UiLogEntry>();
        var reader = new FakeConversationReader();
        var viewModel = CreateViewModel(CreateActiveCase(), reader, logs);

        await WaitForAsync(() => reader.SummariesCallCount == 1 && !GetBooleanProperty(viewModel, "IsConversationListLoading"));

        Assert.Equal("No conversations have been built for this case yet. Run the conversation builder after importing messages.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.True(GetBooleanProperty(viewModel, "IsConversationListEmptyStateVisible"));
        Assert.Equal(0, GetIntProperty(viewModel, "ConversationCount"));
        Assert.Equal(0, reader.ThreadCallCount);
        Assert.Contains(logs, entry => entry.Operation == "conversation_workspace_conversation_list_load_succeeded");
    }

    [Fact]
    public async Task ConversationWorkspaceViewModel_Active_Case_Loads_Conversation_List_And_First_Thread()
    {
        var logs = new List<UiLogEntry>();
        var reader = new FakeConversationReader
        {
            Summaries =
            [
                CreateSummary("conv-002", "Synthetic Conversation B", "sms", "2026-04-28T14:00:00Z", "2026-04-28T15:10:00Z", 2, 1, 0, 4.5, "not_started", "unreviewed"),
                CreateSummary("conv-001", "Synthetic Conversation A", "signal", "2026-04-28T12:00:00Z", "2026-04-28T12:45:00Z", 3, 2, 1, 7.5, "not_started", "needs_review")
            ],
            Threads =
            {
                ["conv-002"] = CreateThread(
                    "conv-002",
                    [
                        CreateMessage("msg-201", "2026-04-28T14:00:00Z", "outgoing", "Sender B", ["Recipient B"], "Thread B one", "sms", "present", "src-201", "Synthetic Source B", "csv_messages", "thread-b", "provider-201"),
                        CreateMessage("msg-202", "2026-04-28T15:10:00Z", "incoming", "Recipient B", ["Sender B"], "Thread B two", "sms", "present", "src-201", "Synthetic Source B", "csv_messages", "thread-b", "provider-202")
                    ]),
                ["conv-001"] = CreateThread(
                    "conv-001",
                    [
                        CreateMessage("msg-101", "2026-04-28T12:00:00Z", "outgoing", "Sender A", ["Recipient A"], "Thread A one", "signal", "present", "src-101", "Synthetic Source A", "csv_messages", "thread-a", "provider-101")
                    ])
            }
        };

        var viewModel = CreateViewModel(CreateActiveCase(), reader, logs);

        await WaitForAsync(() => GetIntProperty(viewModel, "ConversationCount") == 2);
        await WaitForAsync(() => GetIntProperty(viewModel, "ThreadMessageCount") == 2);

        Assert.Equal("2 conversations loaded.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.Equal("2 messages loaded.", GetStringProperty(viewModel, "ThreadStatusMessage"));
        Assert.Equal(1, reader.SummariesCallCount);
        Assert.Equal(1, reader.ThreadCallCount);

        var selectedConversation = GetPropertyValue(viewModel, "SelectedConversation");
        Assert.Equal("conv-002", GetStringProperty(selectedConversation, "ConversationId"));

        var messages = GetCollection(viewModel, "ThreadMessages");
        Assert.Equal(2, messages.Count);
        Assert.Equal("msg-201", GetStringProperty(messages[0], "MessageId"));
        Assert.Contains(logs, entry => entry.Operation == "conversation_workspace_conversation_selected");
        Assert.Contains(logs, entry => entry.Operation == "conversation_workspace_thread_load_succeeded");
    }

    [Fact]
    public async Task ConversationWorkspaceViewModel_Selecting_A_Different_Conversation_Loads_Its_Thread()
    {
        var logs = new List<UiLogEntry>();
        var reader = new FakeConversationReader
        {
            Summaries =
            [
                CreateSummary("conv-002", "Synthetic Conversation B", "sms", "2026-04-28T14:00:00Z", "2026-04-28T15:10:00Z", 2, 1, 0, 4.5, "not_started", "unreviewed"),
                CreateSummary("conv-001", "Synthetic Conversation A", "signal", "2026-04-28T12:00:00Z", "2026-04-28T12:45:00Z", 3, 2, 1, 7.5, "not_started", "needs_review")
            ],
            Threads =
            {
                ["conv-002"] = CreateThread("conv-002", [CreateMessage("msg-201", "2026-04-28T14:00:00Z", "outgoing", "Sender B", ["Recipient B"], "Thread B one", "sms", "present", "src-201", "Synthetic Source B", "csv_messages", "thread-b", "provider-201")]),
                ["conv-001"] = CreateThread(
                    "conv-001",
                    [
                        CreateMessage("msg-101", "2026-04-28T12:00:00Z", "outgoing", "Sender A", ["Recipient A"], "Thread A one", "signal", "present", "src-101", "Synthetic Source A", "csv_messages", "thread-a", "provider-101"),
                        CreateMessage("msg-102", "2026-04-28T12:10:00Z", "incoming", "Recipient A", ["Sender A"], "Thread A two", "signal", "present", "src-101", "Synthetic Source A", "csv_messages", "thread-a", "provider-102")
                    ])
            }
        };

        var viewModel = CreateViewModel(CreateActiveCase(), reader, logs);
        await WaitForAsync(() => GetIntProperty(viewModel, "ConversationCount") == 2);

        var conversations = GetCollection(viewModel, "Conversations");
        SetPropertyValue(viewModel, "SelectedConversation", conversations[1]);

        await WaitForAsync(() => GetIntProperty(viewModel, "ThreadMessageCount") == 2);

        var selectedConversation = GetPropertyValue(viewModel, "SelectedConversation");
        Assert.Equal("conv-001", GetStringProperty(selectedConversation, "ConversationId"));
        Assert.Equal(2, reader.ThreadCallCount);
        Assert.Equal(2, logs.Count(entry => entry.Operation == "conversation_workspace_conversation_selected"));
    }

    [Fact]
    public async Task ConversationWorkspaceViewModel_Selecting_A_Message_Updates_Inspector_With_Safe_Source_Context()
    {
        const string sensitiveToken = "TOP_SECRET_THREAD_BODY";
        var logs = new List<UiLogEntry>();
        var reader = new FakeConversationReader
        {
            Summaries =
            [
                CreateSummary("conv-001", "Synthetic Conversation", "sms", "2026-04-28T12:00:00Z", "2026-04-28T12:45:00Z", 1, 1, 0, 2.5, "not_started", "unreviewed")
            ],
            Threads =
            {
                ["conv-001"] = CreateThread(
                    "conv-001",
                    [
                        CreateMessage("msg-001", "2026-04-28T12:00:00Z", "outgoing", "Sender A", ["Recipient A"], sensitiveToken, "sms", "present", "src-001", sensitiveToken, "csv_messages", "thread-a", "provider-001")
                    ])
            }
        };

        var viewModel = CreateViewModel(CreateActiveCase(), reader, logs);
        await WaitForAsync(() => GetIntProperty(viewModel, "ThreadMessageCount") == 1);

        var messages = GetCollection(viewModel, "ThreadMessages");
        SetPropertyValue(viewModel, "SelectedMessage", messages[0]);

        await WaitForAsync(() =>
        {
            var inspector = GetPropertyValue(viewModel, "CurrentInspector");
            return GetBooleanProperty(inspector, "HasSourceContext");
        });

        var inspector = GetPropertyValue(viewModel, "CurrentInspector");
        Assert.Equal("src-001", GetStringProperty(inspector, "SourceImportIdDisplay"));
        Assert.Equal("csv_messages", GetStringProperty(inspector, "SourceTypeDisplay"));
        Assert.Equal("thread-a", GetStringProperty(inspector, "SourceThreadIdDisplay"));
        Assert.Equal("provider-001", GetStringProperty(inspector, "ProviderMessageIdDisplay"));

        Assert.Contains(logs, entry => entry.Operation == "conversation_workspace_message_selected");
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, entry.Message, StringComparison.Ordinal));
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, string.Join("|", entry.Fields.Values), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConversationWorkspaceViewModel_List_Load_Failure_Shows_Safe_Error()
    {
        const string sensitiveToken = "TOP_SECRET_CONVERSATION_TITLE";
        var logs = new List<UiLogEntry>();
        var reader = new FakeConversationReader
        {
            SummariesException = new InvalidOperationException(sensitiveToken)
        };

        var viewModel = CreateViewModel(CreateActiveCase(), reader, logs);

        await WaitForAsync(() => GetBooleanProperty(viewModel, "HasConversationListError"));

        Assert.Equal("Conversations could not be loaded. Check the case package and try again.", GetStringProperty(viewModel, "ConversationListErrorMessage"));
        Assert.Equal("Conversations could not be loaded. Check the case package and try again.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.Contains(logs, entry => entry.Operation == "conversation_workspace_conversation_list_load_failed");
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, entry.Message, StringComparison.Ordinal));
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, string.Join("|", entry.Fields.Values), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConversationWorkspaceViewModel_Thread_Load_Failure_Shows_Safe_Error()
    {
        const string sensitiveToken = "TOP_SECRET_SOURCE_NAME";
        var logs = new List<UiLogEntry>();
        var reader = new FakeConversationReader
        {
            Summaries =
            [
                CreateSummary("conv-001", "Synthetic Conversation", "sms", "2026-04-28T12:00:00Z", "2026-04-28T12:45:00Z", 1, 1, 0, 2.5, "not_started", "unreviewed")
            ],
            ThreadExceptions =
            {
                ["conv-001"] = new InvalidOperationException(sensitiveToken)
            }
        };

        var viewModel = CreateViewModel(CreateActiveCase(), reader, logs);

        await WaitForAsync(() => GetBooleanProperty(viewModel, "HasThreadError"));

        Assert.Equal("Conversation thread could not be loaded. Check the case package and try again.", GetStringProperty(viewModel, "ThreadErrorMessage"));
        Assert.Equal("Conversation thread could not be loaded. Check the case package and try again.", GetStringProperty(viewModel, "ThreadStatusMessage"));
        Assert.Contains(logs, entry => entry.Operation == "conversation_workspace_thread_load_failed");
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, entry.Message, StringComparison.Ordinal));
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, string.Join("|", entry.Fields.Values), StringComparison.Ordinal));
    }

    private static object CreateViewModel(
        CreateCaseResult? activeCase,
        IConversationReader reader,
        List<UiLogEntry> logs)
    {
        var assembly = ViewModelAssemblyLoader.Load();
        var type = assembly.GetType("DumpLens.App.ViewModels.ConversationWorkspaceViewModel", throwOnError: true)!;
        Action<string, string, string, IReadOnlyDictionary<string, string>?> logAction =
            (operation, correlationId, message, fields) =>
            {
                logs.Add(new UiLogEntry(operation, correlationId, message, fields ?? new Dictionary<string, string>(StringComparer.Ordinal)));
            };

        return Activator.CreateInstance(type, activeCase, reader, logAction)!;
    }

    private static CreateCaseResult CreateActiveCase()
    {
        return new CreateCaseResult
        {
            CaseId = "case-conv-001",
            PackageId = "pkg-conv-001",
            CaseNumber = "DL-CONV-001",
            Title = "Synthetic Conversation Case",
            PackageRootPath = @"O:\Cases\SyntheticConversationCase",
            DatabasePath = @"O:\Cases\SyntheticConversationCase\case.dlensdb",
            ManifestPath = @"O:\Cases\SyntheticConversationCase\manifest.json",
            CreatedAtUtc = DateTimeOffset.Parse("2026-04-28T12:00:00Z"),
            CorrelationId = "case-conv-load"
        };
    }

    private static ConversationSummary CreateSummary(
        string conversationId,
        string title,
        string platform,
        string startTimeUtc,
        string endTimeUtc,
        int messageCount,
        int sourceCount,
        int gapCount,
        double priorityScore,
        string reconciliationStatus,
        string reviewStatus)
    {
        return new ConversationSummary
        {
            ConversationId = conversationId,
            Title = title,
            Platform = platform,
            StartTimeUtc = DateTimeOffset.Parse(startTimeUtc),
            EndTimeUtc = DateTimeOffset.Parse(endTimeUtc),
            MessageCount = messageCount,
            SourceCount = sourceCount,
            GapCount = gapCount,
            PriorityScore = priorityScore,
            ReconciliationStatus = reconciliationStatus,
            ReviewStatus = reviewStatus
        };
    }

    private static ConversationThread CreateThread(
        string conversationId,
        IReadOnlyList<ConversationThreadMessage> messages)
    {
        return new ConversationThread
        {
            ConversationId = conversationId,
            Messages = messages
        };
    }

    private static ConversationThreadMessage CreateMessage(
        string messageId,
        string eventTimeUtc,
        string direction,
        string senderLabel,
        IReadOnlyList<string> recipients,
        string messageBody,
        string platform,
        string deletedStatus,
        string sourceImportId,
        string sourceName,
        string sourceType,
        string sourceThreadId,
        string providerMessageId)
    {
        return new ConversationThreadMessage
        {
            MessageId = messageId,
            EventTimeUtc = DateTimeOffset.Parse(eventTimeUtc),
            CreatedAtUtc = DateTimeOffset.Parse(eventTimeUtc).AddMinutes(1),
            Direction = direction,
            SenderDisplayLabel = senderLabel,
            RecipientDisplayLabels = recipients,
            MessageBody = messageBody,
            Platform = platform,
            DeletedStatus = deletedStatus,
            HasSourceReference = true,
            SourceContext = new ConversationSourceContext
            {
                SourceImportId = sourceImportId,
                SourceName = sourceName,
                SourceType = sourceType,
                Platform = platform,
                OriginalFilename = $"{sourceImportId}.csv",
                SourceArtifactId = $"{messageId}-artifact",
                ArtifactLocator = $"row:{messageId}",
                ProviderMessageId = providerMessageId,
                SourceThreadId = sourceThreadId,
                MessageHashPrefix = "abcdef123456"
            }
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
        return Assert.IsType<string>(GetPropertyValue(instance, propertyName));
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
                throw new TimeoutException("Timed out waiting for the conversation workspace state to update.");
            }

            await Task.Delay(25);
        }
    }

    private sealed record UiLogEntry(
        string Operation,
        string CorrelationId,
        string Message,
        IReadOnlyDictionary<string, string> Fields);

    private sealed class FakeConversationReader : IConversationReader
    {
        public IReadOnlyList<ConversationSummary> Summaries { get; init; } = Array.Empty<ConversationSummary>();

        public Exception? SummariesException { get; init; }

        public int SummariesCallCount { get; private set; }

        public Dictionary<string, ConversationThread> Threads { get; init; } = new(StringComparer.Ordinal);

        public Dictionary<string, Exception> ThreadExceptions { get; init; } = new(StringComparer.Ordinal);

        public int ThreadCallCount { get; private set; }

        public Task<IReadOnlyList<ConversationSummary>> GetSummariesAsync(
            LoadConversationSummariesRequest request,
            CancellationToken cancellationToken = default)
        {
            SummariesCallCount++;
            if (SummariesException is not null)
            {
                throw SummariesException;
            }

            return Task.FromResult(Summaries);
        }

        public Task<ConversationThread?> GetThreadAsync(
            LoadConversationThreadRequest request,
            CancellationToken cancellationToken = default)
        {
            ThreadCallCount++;
            if (ThreadExceptions.TryGetValue(request.ConversationId, out var exception))
            {
                throw exception;
            }

            Threads.TryGetValue(request.ConversationId, out var thread);
            return Task.FromResult(thread);
        }
    }
}
