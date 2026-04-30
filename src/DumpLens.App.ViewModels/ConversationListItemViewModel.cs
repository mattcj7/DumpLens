using System.Globalization;
using DumpLens.Application.Conversations;

namespace DumpLens.App.ViewModels;

public sealed class ConversationListItemViewModel
{
    public ConversationListItemViewModel(ConversationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        ConversationId = summary.ConversationId;
        Title = summary.Title;
        Platform = summary.Platform;
        StartTimeUtc = summary.StartTimeUtc;
        EndTimeUtc = summary.EndTimeUtc;
        MessageCount = summary.MessageCount;
        SourceCount = summary.SourceCount;
        GapCount = summary.GapCount;
        PriorityScore = summary.PriorityScore;
        ReconciliationStatus = summary.ReconciliationStatus;
        ReviewStatus = summary.ReviewStatus;
        PlatformDisplay = string.IsNullOrWhiteSpace(summary.Platform) ? "-" : summary.Platform!;
        StartTimeDisplay = FormatUtc(summary.StartTimeUtc);
        EndTimeDisplay = FormatUtc(summary.EndTimeUtc);
        MessageCountDisplay = summary.MessageCount.ToString("#,0", CultureInfo.InvariantCulture);
        SourceCountDisplay = summary.SourceCount.ToString("#,0", CultureInfo.InvariantCulture);
        GapCountDisplay = summary.GapCount.ToString("#,0", CultureInfo.InvariantCulture);
        PriorityScoreDisplay = summary.PriorityScore.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public string ConversationId { get; }

    public string EndTimeDisplay { get; }

    public DateTimeOffset? EndTimeUtc { get; }

    public int GapCount { get; }

    public string GapCountDisplay { get; }

    public int MessageCount { get; }

    public string MessageCountDisplay { get; }

    public string PlatformDisplay { get; }

    public double PriorityScore { get; }

    public string PriorityScoreDisplay { get; }

    public string ReconciliationStatus { get; }

    public string ReviewStatus { get; }

    public int SourceCount { get; }

    public string SourceCountDisplay { get; }

    public string StartTimeDisplay { get; }

    public DateTimeOffset? StartTimeUtc { get; }

    public string Title { get; }

    public string? Platform { get; }

    private static string FormatUtc(DateTimeOffset? value)
    {
        return value.HasValue
            ? value.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
            : "-";
    }
}
