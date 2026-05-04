using System.Globalization;

namespace DumpLens.App.ViewModels;

public sealed class SearchInspectorViewModel : InspectorViewModelBase
{
    private SearchInspectorViewModel(
        string description,
        string stateMessage)
        : base("Search Result Detail", description)
    {
        StateMessage = stateMessage;
        MessageIdDisplay = "Not recorded";
        ConversationIdDisplay = "Not recorded";
        SourceImportIdDisplay = "Not recorded";
        SourceArtifactIdDisplay = "Not recorded";
        ProviderMessageIdDisplay = "Not recorded";
        SourceThreadIdDisplay = "Not recorded";
        EventTimeUtcDisplay = "Not recorded";
        PlatformDisplay = "Not recorded";
        DirectionDisplay = "Not recorded";
        DeletedStatusDisplay = "Not recorded";
        RankDisplay = "Not available";
    }

    public string ConversationIdDisplay { get; init; }

    public string DeletedStatusDisplay { get; init; }

    public string DirectionDisplay { get; init; }

    public string EventTimeUtcDisplay { get; init; }

    public bool HasSelection { get; init; }

    public string MessageIdDisplay { get; init; }

    public string PlatformDisplay { get; init; }

    public string ProviderMessageIdDisplay { get; init; }

    public string RankDisplay { get; init; }

    public string SourceArtifactIdDisplay { get; init; }

    public string SourceImportIdDisplay { get; init; }

    public string SourceThreadIdDisplay { get; init; }

    public string StateMessage { get; }

    public static SearchInspectorViewModel CreateActiveCaseMissing()
    {
        return new SearchInspectorViewModel(
            "Create or open a case to inspect search result context.",
            "Create or open a case to search messages.");
    }

    public static SearchInspectorViewModel CreateNoResultSelected()
    {
        return new SearchInspectorViewModel(
            "Select a search result to inspect safe message and source context.",
            "No search result is selected.");
    }

    public static SearchInspectorViewModel FromResult(SearchResultItemViewModel result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new SearchInspectorViewModel(
            "Safe source and message reference context loaded for the selected result.",
            "Safe source and message reference context loaded for the selected result.")
        {
            HasSelection = true,
            MessageIdDisplay = result.MessageId,
            ConversationIdDisplay = FormatOptional(result.ConversationId, "Not recorded"),
            SourceImportIdDisplay = result.SourceImportId,
            SourceArtifactIdDisplay = FormatOptional(result.SourceArtifactId, "Not recorded"),
            ProviderMessageIdDisplay = FormatOptional(result.ProviderMessageId, "Not recorded"),
            SourceThreadIdDisplay = FormatOptional(result.SourceThreadId, "Not recorded"),
            EventTimeUtcDisplay = result.EventTimeUtc?.ToUniversalTime().ToString(
                    "yyyy-MM-dd HH:mm:ss 'UTC'",
                    CultureInfo.InvariantCulture)
                ?? "Not recorded",
            PlatformDisplay = FormatOptional(result.Platform, "Not recorded"),
            DirectionDisplay = FormatOptional(result.Direction, "Not recorded"),
            DeletedStatusDisplay = result.DeletedStatus,
            RankDisplay = result.Rank.HasValue
                ? result.Rank.Value.ToString("0.000", CultureInfo.InvariantCulture)
                : "Not available"
        };
    }

    private static string FormatOptional(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value;
    }
}
