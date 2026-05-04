using System.Globalization;
using DumpLens.Application.Search;

namespace DumpLens.App.ViewModels;

public sealed class SearchResultItemViewModel
{
    public SearchResultItemViewModel(MessageSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        MessageId = result.MessageId;
        ConversationId = NormalizeOptional(result.ConversationId);
        SourceImportId = result.SourceImportId;
        SourceArtifactId = NormalizeOptional(result.SourceArtifactId);
        ProviderMessageId = NormalizeOptional(result.ProviderMessageId);
        SourceThreadId = NormalizeOptional(result.SourceThreadId);
        EventTimeUtc = result.EventTimeUtc;
        Platform = NormalizeOptional(result.Platform);
        Direction = NormalizeOptional(result.Direction);
        DeletedStatus = result.DeletedStatus;
        Snippet = NormalizeOptional(result.Snippet);
        Rank = result.Rank;
    }

    public string ConversationDisplay => BuildDisplay("Conversation", ConversationId, "not recorded");

    public string? ConversationId { get; }

    public string DeletedStatus { get; }

    public string DeletedStatusDisplay => DeletedStatus;

    public string DirectionDisplay => FormatOptional(Direction, "Direction not recorded");

    public string? Direction { get; }

    public DateTimeOffset? EventTimeUtc { get; }

    public string MessageId { get; }

    public string PlatformDisplay => FormatOptional(Platform, "Platform not recorded");

    public string? Platform { get; }

    public string? ProviderMessageId { get; }

    public string RankDisplay => Rank.HasValue
        ? string.Format(CultureInfo.InvariantCulture, "Rank: {0:0.000}", Rank.Value)
        : "Rank: not available";

    public double? Rank { get; }

    public string SnippetDisplay => FormatOptional(Snippet, "No message snippet available.");

    public string? Snippet { get; }

    public string SourceArtifactDisplay => BuildDisplay("Source artifact", SourceArtifactId, "not recorded");

    public string? SourceArtifactId { get; }

    public string SourceImportDisplay => BuildDisplay("Source import", SourceImportId, "not recorded");

    public string SourceImportId { get; }

    public string SourceThreadDisplay => BuildDisplay("Thread", SourceThreadId, "not recorded");

    public string? SourceThreadId { get; }

    public string TimestampDisplay => EventTimeUtc?.ToUniversalTime().ToString(
            "yyyy-MM-dd HH:mm:ss 'UTC'",
            CultureInfo.InvariantCulture)
        ?? "Timestamp not recorded";

    private static string FormatOptional(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value;
    }

    private static string BuildDisplay(string label, string? value, string fallback)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}: {1}",
            label,
            FormatOptional(value, fallback));
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
