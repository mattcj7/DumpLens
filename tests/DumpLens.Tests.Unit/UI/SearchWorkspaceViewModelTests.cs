using System.Collections;
using System.Reflection;
using System.Windows.Input;
using DumpLens.Application.Cases;
using DumpLens.Application.Search;
using DumpLens.Application.SourceReferences;

namespace DumpLens.Tests.Unit.UI;

public sealed class SearchWorkspaceViewModelTests
{
    [Fact]
    public void SearchWorkspaceViewModel_No_Active_Case_Shows_Required_Empty_State()
    {
        var logs = new List<UiLogEntry>();
        var service = new FakeMessageSearchIndexService();
        var viewModel = CreateViewModel(activeCase: null, service, logs);

        Assert.False(GetBooleanProperty(viewModel, "HasActiveCase"));
        Assert.True(GetBooleanProperty(viewModel, "IsNoActiveCaseVisible"));
        Assert.Equal("Create or open a case to search messages.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.Equal(0, GetIntProperty(viewModel, "SearchResultCount"));
        Assert.Equal(0, service.SearchCallCount);
        Assert.Equal(0, service.RebuildCallCount);

        var inspector = GetPropertyValue(viewModel, "CurrentInspector");
        Assert.Equal("Create or open a case to inspect safe source references.", GetStringProperty(inspector, "Description"));

        Assert.Contains(logs, entry => entry.Operation == "search_workspace_opened");
        Assert.Contains(logs, entry => entry.Operation == "search_workspace_active_case_missing");
    }

    [Fact]
    public async Task SearchWorkspaceViewModel_Empty_Query_Shows_Validation_And_Does_Not_Call_Service()
    {
        var logs = new List<UiLogEntry>();
        var service = new FakeMessageSearchIndexService();
        var viewModel = CreateViewModel(CreateActiveCase(), service, logs);

        var command = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "SearchCommand"));
        command.Execute(null);

        await WaitForAsync(() => GetBooleanProperty(viewModel, "HasValidationMessage"));

        Assert.Equal("Enter one or more search terms.", GetStringProperty(viewModel, "ValidationMessage"));
        Assert.Equal("Enter one or more search terms.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.Equal(0, service.SearchCallCount);
        Assert.Contains(logs, entry => entry.Operation == "search_workspace_search_requested");
        Assert.Contains(logs, entry => entry.Operation == "search_workspace_search_succeeded");
    }

    [Fact]
    public async Task SearchWorkspaceViewModel_Successful_Search_Populates_Results()
    {
        var logs = new List<UiLogEntry>();
        var service = new FakeMessageSearchIndexService
        {
            SearchResultFactory = _ => CreateSearchResult(
                isQueryValid: true,
                resultCount: 1,
                results:
                [
                    CreateMessageSearchResult(
                        messageId: "msg-search-001",
                        snippet: "[[alpha]] synthetic result",
                        conversationId: "conv-search-001",
                        sourceImportId: "src-search-001",
                        sourceArtifactId: "art-search-001",
                        sourceThreadId: "thread-search-001",
                        providerMessageId: "provider-search-001")
                ])
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs);
        SetPropertyValue(viewModel, "SearchQueryText", "alpha");

        var command = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "SearchCommand"));
        command.Execute(null);

        await WaitForAsync(() => GetIntProperty(viewModel, "SearchResultCount") == 1);

        Assert.Equal(1, service.SearchCallCount);
        Assert.Equal("1 matching message found.", GetStringProperty(viewModel, "StatusMessage"));

        var results = GetCollection(viewModel, "Results");
        Assert.Equal("msg-search-001", GetStringProperty(results[0], "MessageId"));
        Assert.Equal("Source import: src-search-001", GetStringProperty(results[0], "SourceImportDisplay"));
        Assert.Contains(logs, entry => entry.Operation == "search_workspace_search_succeeded");
    }

    [Fact]
    public async Task SearchWorkspaceViewModel_No_Results_Shows_Expected_Empty_State()
    {
        var logs = new List<UiLogEntry>();
        var service = new FakeMessageSearchIndexService
        {
            SearchResultFactory = static _ => CreateSearchResult(isQueryValid: true, resultCount: 0, results: [])
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs);
        SetPropertyValue(viewModel, "SearchQueryText", "missing");

        var command = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "SearchCommand"));
        command.Execute(null);

        await WaitForAsync(() => GetBooleanProperty(viewModel, "IsResultsEmptyStateVisible"));

        Assert.Equal("No matching messages found.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.Equal("No matching messages found.", GetStringProperty(viewModel, "ResultsEmptyStateMessage"));
        Assert.Equal(0, GetIntProperty(viewModel, "SearchResultCount"));
    }

    [Fact]
    public async Task SearchWorkspaceViewModel_Special_Character_Query_Shows_Safe_Validation()
    {
        var logs = new List<UiLogEntry>();
        var service = new FakeMessageSearchIndexService
        {
            SearchResultFactory = static _ => CreateSearchResult(
                isQueryValid: false,
                resultCount: 0,
                validationErrorCode: MessageSearchValidationCodes.UnsupportedQuery,
                validationMessage: "The search query did not contain any searchable terms.",
                results: [])
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs);
        SetPropertyValue(viewModel, "SearchQueryText", "!!! ???");

        var command = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "SearchCommand"));
        command.Execute(null);

        await WaitForAsync(() => GetBooleanProperty(viewModel, "HasValidationMessage"));

        Assert.Equal("The search query did not contain any searchable terms.", GetStringProperty(viewModel, "ValidationMessage"));
        Assert.Equal(1, service.SearchCallCount);
        Assert.Contains(logs, entry => entry.Operation == "search_workspace_search_succeeded");
    }

    [Fact]
    public async Task SearchWorkspaceViewModel_Search_Failure_Shows_Safe_Error()
    {
        const string sensitiveToken = "TOP_SECRET_SEARCH_QUERY";
        var logs = new List<UiLogEntry>();
        var service = new FakeMessageSearchIndexService
        {
            SearchException = new InvalidOperationException(sensitiveToken)
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs);
        SetPropertyValue(viewModel, "SearchQueryText", "alpha");

        var command = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "SearchCommand"));
        command.Execute(null);

        await WaitForAsync(() => GetBooleanProperty(viewModel, "HasError"));

        Assert.Equal("Search could not be completed. Try again or rebuild the search index.", GetStringProperty(viewModel, "ErrorMessage"));
        Assert.Equal("Search could not be completed. Try again or rebuild the search index.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.Contains(logs, entry => entry.Operation == "search_workspace_search_failed");
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, entry.Message, StringComparison.Ordinal));
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, string.Join("|", entry.Fields.Values), StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchWorkspaceViewModel_Rebuild_Success_Updates_Status()
    {
        var logs = new List<UiLogEntry>();
        var service = new FakeMessageSearchIndexService
        {
            RebuildResult = CreateRebuildResult(indexedCount: 4)
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs);
        var command = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "RebuildSearchIndexCommand"));
        command.Execute(null);

        await WaitForAsync(() => service.RebuildCallCount == 1 && !GetBooleanProperty(viewModel, "IsBusy"));

        Assert.Equal("Search index rebuilt for this case. Indexed 4 messages.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.Contains(logs, entry => entry.Operation == "search_workspace_rebuild_requested");
        Assert.Contains(logs, entry => entry.Operation == "search_workspace_rebuild_succeeded");
    }

    [Fact]
    public async Task SearchWorkspaceViewModel_Rebuild_Failure_Shows_Safe_Error()
    {
        const string sensitiveToken = "TOP_SECRET_SNIPPET";
        var logs = new List<UiLogEntry>();
        var service = new FakeMessageSearchIndexService
        {
            RebuildException = new InvalidOperationException(sensitiveToken)
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs);
        var command = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "RebuildSearchIndexCommand"));
        command.Execute(null);

        await WaitForAsync(() => GetBooleanProperty(viewModel, "HasError"));

        Assert.Equal("Search index rebuild could not be completed. Check the case package and try again.", GetStringProperty(viewModel, "ErrorMessage"));
        Assert.Equal("Search index rebuild could not be completed. Check the case package and try again.", GetStringProperty(viewModel, "StatusMessage"));
        Assert.Contains(logs, entry => entry.Operation == "search_workspace_rebuild_failed");
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, entry.Message, StringComparison.Ordinal));
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveToken, string.Join("|", entry.Fields.Values), StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchWorkspaceViewModel_Selecting_Result_Updates_Inspector()
    {
        var logs = new List<UiLogEntry>();
        var sourceReferenceReader = new FakeSourceReferenceReader
        {
            DetailResult = CreateSourceReferenceDetail(
                sourceImportId: "src-search-002",
                sourceArtifactId: "art-search-002",
                messageId: "msg-search-002",
                providerMessageId: "provider-search-002",
                sourceThreadId: "thread-search-002")
        };
        var service = new FakeMessageSearchIndexService
        {
            SearchResultFactory = static _ => CreateSearchResult(
                isQueryValid: true,
                resultCount: 1,
                results:
                [
                    CreateMessageSearchResult(
                        messageId: "msg-search-002",
                        snippet: "[[bravo]] synthetic result",
                        conversationId: "conv-search-002",
                        sourceImportId: "src-search-002",
                        sourceArtifactId: "art-search-002",
                        sourceThreadId: "thread-search-002",
                        providerMessageId: "provider-search-002")
                ])
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs, sourceReferenceReader);
        SetPropertyValue(viewModel, "SearchQueryText", "bravo");

        var command = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "SearchCommand"));
        command.Execute(null);

        await WaitForAsync(() => GetIntProperty(viewModel, "SearchResultCount") == 1);

        var result = GetCollection(viewModel, "Results")[0];
        SetPropertyValue(viewModel, "SelectedResult", result);

        await WaitForAsync(() =>
        {
            var inspector = GetPropertyValue(viewModel, "CurrentInspector");
            return GetIntProperty(inspector, "Sections.Count") == 3;
        });

        var inspector = GetPropertyValue(viewModel, "CurrentInspector");
        Assert.Equal("Source reference loaded.", GetStringProperty(inspector, "StateMessage"));
        Assert.Equal("src-search-002", GetFieldValue(inspector, "Source Reference", "Source Import ID"));
        Assert.Equal("art-search-002", GetFieldValue(inspector, "Artifact Reference", "Source Artifact ID"));
        Assert.Equal("provider-search-002", GetFieldValue(inspector, "Message Reference", "Provider Message ID"));
        Assert.Equal("thread-search-002", GetFieldValue(inspector, "Message Reference", "Source Thread ID"));
        Assert.Contains(logs, entry => entry.Operation == "search_workspace_result_selected");
        Assert.Contains(logs, entry => entry.Operation == "source_reference_inspector_requested");
        Assert.Contains(logs, entry => entry.Operation == "source_reference_inspector_loaded");
    }

    [Fact]
    public async Task SearchWorkspaceViewModel_Missing_Source_Reference_Shows_Safe_Error()
    {
        var logs = new List<UiLogEntry>();
        var sourceReferenceReader = new FakeSourceReferenceReader
        {
            DetailResult = null
        };
        var service = new FakeMessageSearchIndexService
        {
            SearchResultFactory = static _ => CreateSearchResult(
                isQueryValid: true,
                resultCount: 1,
                results:
                [
                    CreateMessageSearchResult(
                        messageId: "msg-search-004",
                        snippet: "[[charlie]] synthetic result",
                        conversationId: "conv-search-004",
                        sourceImportId: "src-search-004",
                        sourceArtifactId: "art-search-004",
                        sourceThreadId: "thread-search-004",
                        providerMessageId: "provider-search-004")
                ])
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs, sourceReferenceReader);
        SetPropertyValue(viewModel, "SearchQueryText", "charlie");

        var command = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "SearchCommand"));
        command.Execute(null);

        await WaitForAsync(() => GetIntProperty(viewModel, "SearchResultCount") == 1);

        var result = GetCollection(viewModel, "Results")[0];
        SetPropertyValue(viewModel, "SelectedResult", result);

        await WaitForAsync(() =>
        {
            var inspector = GetPropertyValue(viewModel, "CurrentInspector");
            return string.Equals(GetStringProperty(inspector, "StateMessage"), "Source reference could not be loaded.", StringComparison.Ordinal);
        });

        var inspector = GetPropertyValue(viewModel, "CurrentInspector");
        Assert.Equal("Source reference could not be loaded.", GetStringProperty(inspector, "StateMessage"));
        Assert.Contains(logs, entry => entry.Operation == "source_reference_inspector_missing");
    }

    [Fact]
    public async Task SearchWorkspaceViewModel_Duplicate_Search_And_Rebuild_Are_Blocked_While_Busy()
    {
        var logs = new List<UiLogEntry>();
        var searchCompletion = new TaskCompletionSource<SearchMessagesResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeMessageSearchIndexService
        {
            SearchTask = searchCompletion.Task,
            RebuildResult = CreateRebuildResult(indexedCount: 2)
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs);
        SetPropertyValue(viewModel, "SearchQueryText", "hold");

        var searchCommand = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "SearchCommand"));
        var rebuildCommand = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "RebuildSearchIndexCommand"));

        searchCommand.Execute(null);
        await WaitForAsync(() => service.SearchCallCount == 1 && GetBooleanProperty(viewModel, "IsBusy"));

        Assert.False(searchCommand.CanExecute(null));
        Assert.False(rebuildCommand.CanExecute(null));

        searchCommand.Execute(null);
        rebuildCommand.Execute(null);

        await Task.Delay(100);

        Assert.Equal(1, service.SearchCallCount);
        Assert.Equal(0, service.RebuildCallCount);

        searchCompletion.SetResult(CreateSearchResult(isQueryValid: true, resultCount: 0, results: []));

        await WaitForAsync(() => !GetBooleanProperty(viewModel, "IsBusy"));
    }

    [Fact]
    public async Task SearchWorkspaceViewModel_Logs_Do_Not_Include_Query_Or_Snippet_Content()
    {
        const string sensitiveQuery = "TOP_SECRET_QUERY_8035551212";
        const string sensitiveSnippet = "TOP_SECRET_SNIPPET_jane@example.com";
        var logs = new List<UiLogEntry>();
        var service = new FakeMessageSearchIndexService
        {
            SearchResultFactory = static _ => CreateSearchResult(
                isQueryValid: true,
                resultCount: 1,
                results:
                [
                    CreateMessageSearchResult(
                        messageId: "msg-search-003",
                        snippet: sensitiveSnippet,
                        conversationId: "conv-search-003",
                        sourceImportId: "src-search-003",
                        sourceArtifactId: "art-search-003",
                        sourceThreadId: "thread-search-003",
                        providerMessageId: "provider-search-003")
                ])
        };

        var viewModel = CreateViewModel(CreateActiveCase(), service, logs);
        SetPropertyValue(viewModel, "SearchQueryText", sensitiveQuery);

        var searchCommand = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(viewModel, "SearchCommand"));
        searchCommand.Execute(null);

        await WaitForAsync(() => GetIntProperty(viewModel, "SearchResultCount") == 1);

        var result = GetCollection(viewModel, "Results")[0];
        SetPropertyValue(viewModel, "SelectedResult", result);

        await WaitForAsync(() => logs.Any(entry => entry.Operation == "search_workspace_result_selected"));

        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveQuery, entry.Message, StringComparison.Ordinal));
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveSnippet, entry.Message, StringComparison.Ordinal));
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveQuery, string.Join("|", entry.Fields.Values), StringComparison.Ordinal));
        Assert.All(logs, entry => Assert.DoesNotContain(sensitiveSnippet, string.Join("|", entry.Fields.Values), StringComparison.Ordinal));
    }

    private static SearchMessagesResult CreateSearchResult(
        bool isQueryValid,
        int resultCount,
        IReadOnlyList<MessageSearchResult> results,
        string? validationErrorCode = null,
        string? validationMessage = null)
    {
        return new SearchMessagesResult
        {
            CaseId = "case-search-001",
            IsQueryValid = isQueryValid,
            ValidationErrorCode = validationErrorCode,
            ValidationMessage = validationMessage,
            ResultCount = resultCount,
            Results = results,
            StartedAtUtc = DateTimeOffset.Parse("2026-05-01T12:00:00Z"),
            CompletedAtUtc = DateTimeOffset.Parse("2026-05-01T12:00:01Z")
        };
    }

    private static MessageSearchResult CreateMessageSearchResult(
        string messageId,
        string snippet,
        string conversationId,
        string sourceImportId,
        string sourceArtifactId,
        string sourceThreadId,
        string providerMessageId)
    {
        return new MessageSearchResult
        {
            CaseId = "case-search-001",
            MessageId = messageId,
            ConversationId = conversationId,
            SourceImportId = sourceImportId,
            SourceArtifactId = sourceArtifactId,
            ProviderMessageId = providerMessageId,
            SourceThreadId = sourceThreadId,
            EventTimeUtc = DateTimeOffset.Parse("2026-05-01T09:30:00Z"),
            Direction = "outgoing",
            Platform = "sms",
            DeletedStatus = "present",
            Snippet = snippet,
            Rank = 0.245
        };
    }

    private static RebuildMessageSearchIndexResult CreateRebuildResult(int indexedCount)
    {
        return new RebuildMessageSearchIndexResult
        {
            CaseId = "case-search-001",
            IndexedCount = indexedCount,
            StartedAtUtc = DateTimeOffset.Parse("2026-05-01T12:00:00Z"),
            CompletedAtUtc = DateTimeOffset.Parse("2026-05-01T12:00:02Z")
        };
    }

    private static SourceReferenceDetail CreateSourceReferenceDetail(
        string sourceImportId,
        string? sourceArtifactId,
        string? messageId,
        string? providerMessageId = null,
        string? sourceThreadId = null)
    {
        return new SourceReferenceDetail
        {
            CaseId = "case-search-001",
            SourceImportId = sourceImportId,
            SourceName = "Synthetic Search Source",
            SourceType = "csv_messages",
            Platform = "sms",
            ImportStatus = "imported",
            OriginalFilename = $"{sourceImportId}.csv",
            StoredRelativePath = $"imports/source_{sourceImportId}/original/{sourceImportId}.csv",
            FileSizeBytes = 2048,
            FileSha256 = "abcdef123456abcdef123456abcdef123456abcdef123456abcdef123456abcd",
            ImportedAtUtc = DateTimeOffset.Parse("2026-05-01T12:00:00Z"),
            HasSourceMetadata = true,
            WasArtifactReferenceRequested = sourceArtifactId is not null,
            WasMessageReferenceRequested = messageId is not null,
            ArtifactReference = sourceArtifactId is null
                ? null
                : new SourceArtifactReferenceDetail
                {
                    SourceArtifactId = sourceArtifactId,
                    ArtifactType = "message_row",
                    ArtifactLocator = $"row:{sourceArtifactId}",
                    HasOriginalMetadata = true
                },
            MessageReference = messageId is null
                ? null
                : new MessageSourceReferenceDetail
                {
                    MessageId = messageId,
                    SourceArtifactId = sourceArtifactId,
                    ProviderMessageId = providerMessageId,
                    SourceThreadId = sourceThreadId,
                    EventTimeUtc = DateTimeOffset.Parse("2026-05-01T09:30:00Z"),
                    DeletedStatus = "present",
                    MessageHashPrefix = "abcdef123456",
                    HasOriginalMetadata = true
                }
        };
    }

    private static object CreateViewModel(
        CreateCaseResult? activeCase,
        IMessageSearchIndexService service,
        List<UiLogEntry> logs,
        ISourceReferenceReader? sourceReferenceReader = null)
    {
        var assembly = ViewModelAssemblyLoader.Load();
        var type = assembly.GetType("DumpLens.App.ViewModels.SearchWorkspaceViewModel", throwOnError: true)!;
        Action<string, string, string, IReadOnlyDictionary<string, string>?> logAction =
            (operation, correlationId, message, fields) =>
            {
                logs.Add(new UiLogEntry(operation, correlationId, message, fields ?? new Dictionary<string, string>(StringComparer.Ordinal)));
            };

        return Activator.CreateInstance(type, activeCase, service, sourceReferenceReader ?? new FakeSourceReferenceReader(), logAction)!;
    }

    private static CreateCaseResult CreateActiveCase()
    {
        return new CreateCaseResult
        {
            CaseId = "case-search-001",
            PackageId = "pkg-search-001",
            CaseNumber = "DL-SEARCH-001",
            Title = "Synthetic Search Case",
            PackageRootPath = @"O:\Cases\SyntheticSearchCase",
            DatabasePath = @"O:\Cases\SyntheticSearchCase\case.dlensdb",
            ManifestPath = @"O:\Cases\SyntheticSearchCase\manifest.json",
            CreatedAtUtc = DateTimeOffset.Parse("2026-05-01T12:00:00Z"),
            CorrelationId = "case-search-load"
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
        if (propertyName.Contains('.', StringComparison.Ordinal))
        {
            return Assert.IsType<int>(GetNestedPropertyValue(instance, propertyName));
        }

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

    private static object GetNestedPropertyValue(object instance, string propertyPath)
    {
        var current = instance;
        foreach (var segment in propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            current = GetPropertyValue(current, segment);
        }

        return current;
    }

    private static string GetFieldValue(object inspector, string sectionTitle, string fieldLabel)
    {
        var sections = GetCollection(inspector, "Sections");
        var section = sections.Single(item => string.Equals(GetStringProperty(item, "Title"), sectionTitle, StringComparison.Ordinal));
        var fields = GetCollection(section, "Fields");
        var field = fields.Single(item => string.Equals(GetStringProperty(item, "Label"), fieldLabel, StringComparison.Ordinal));
        return GetStringProperty(field, "Value");
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
                throw new TimeoutException("Timed out waiting for the search workspace state to update.");
            }

            await Task.Delay(25);
        }
    }

    private sealed record UiLogEntry(
        string Operation,
        string CorrelationId,
        string Message,
        IReadOnlyDictionary<string, string> Fields);

    private sealed class FakeMessageSearchIndexService : IMessageSearchIndexService
    {
        public Exception? RebuildException { get; init; }

        public RebuildMessageSearchIndexResult? RebuildResult { get; init; }

        public int RebuildCallCount { get; private set; }

        public Exception? SearchException { get; init; }

        public int SearchCallCount { get; private set; }

        public Func<SearchMessagesRequest, SearchMessagesResult>? SearchResultFactory { get; init; }

        public Task<SearchMessagesResult>? SearchTask { get; init; }

        public Task<RebuildMessageSearchIndexResult> RebuildAsync(
            RebuildMessageSearchIndexRequest request,
            CancellationToken cancellationToken = default)
        {
            RebuildCallCount++;
            if (RebuildException is not null)
            {
                throw RebuildException;
            }

            return Task.FromResult(RebuildResult ?? CreateRebuildResult(indexedCount: 0));
        }

        public Task<SearchMessagesResult> SearchAsync(
            SearchMessagesRequest request,
            CancellationToken cancellationToken = default)
        {
            SearchCallCount++;
            if (SearchException is not null)
            {
                throw SearchException;
            }

            if (SearchTask is not null)
            {
                return SearchTask;
            }

            return Task.FromResult(SearchResultFactory?.Invoke(request) ?? CreateSearchResult(isQueryValid: true, resultCount: 0, results: []));
        }
    }

    private sealed class FakeSourceReferenceReader : ISourceReferenceReader
    {
        public SourceReferenceDetail? DetailResult { get; init; }

        public Exception? Exception { get; init; }

        public Task<SourceReferenceDetail?> LoadAsync(
            LoadSourceReferenceRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(DetailResult);
        }
    }
}
