namespace DumpLens.App.ViewModels;

public sealed class ConversationInspectorViewModel : InspectorViewModelBase
{
    private ConversationInspectorViewModel(string description)
        : base("Source Context", description)
    {
        StateMessage = description;
    }

    public string ArtifactLocatorDisplay { get; private init; } = "-";

    public string ConversationEndTimeDisplay { get; private init; } = "-";

    public string ConversationGapCountDisplay { get; private init; } = "0";

    public string ConversationMessageCountDisplay { get; private init; } = "0";

    public string ConversationPlatformDisplay { get; private init; } = "-";

    public string ConversationPriorityScoreDisplay { get; private init; } = "0";

    public string ConversationReconciliationStatus { get; private init; } = "-";

    public string ConversationReviewStatus { get; private init; } = "-";

    public string ConversationSourceCountDisplay { get; private init; } = "0";

    public string ConversationStartTimeDisplay { get; private init; } = "-";

    public string ConversationTitle { get; private init; } = "-";

    public string DirectionDisplay { get; private init; } = "-";

    public bool HasConversationSummary { get; private init; }

    public bool HasMessageContext { get; private init; }

    public bool HasSourceContext { get; private init; }

    public string MessageHashPrefixDisplay { get; private init; } = "-";

    public string MessageIdDisplay { get; private init; } = "-";

    public string OriginalFilenameDisplay { get; private init; } = "-";

    public string PlatformDisplay { get; private init; } = "-";

    public string ProviderMessageIdDisplay { get; private init; } = "-";

    public string RecipientDisplay { get; private init; } = "-";

    public string SelectedMessageSenderDisplay { get; private init; } = "-";

    public string SelectedMessageTimestampDisplay { get; private init; } = "-";

    public string SourceArtifactIdDisplay { get; private init; } = "-";

    public string SourceImportIdDisplay { get; private init; } = "-";

    public string SourceNameDisplay { get; private init; } = "-";

    public string SourceThreadIdDisplay { get; private init; } = "-";

    public string SourceTypeDisplay { get; private init; } = "-";

    public string StateMessage { get; private init; } = "-";

    public static ConversationInspectorViewModel CreateActiveCaseMissing()
    {
        return new ConversationInspectorViewModel("Create or open a case to inspect conversation source context.");
    }

    public static ConversationInspectorViewModel CreateConversationLoadFailure()
    {
        return new ConversationInspectorViewModel("Conversation thread details could not be loaded safely.");
    }

    public static ConversationInspectorViewModel CreateNoConversationSelected()
    {
        return new ConversationInspectorViewModel("Select a conversation to inspect its thread and source context.");
    }

    public static ConversationInspectorViewModel FromConversation(ConversationListItemViewModel conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        return new ConversationInspectorViewModel("Select a message to inspect safe source context.")
        {
            HasConversationSummary = true,
            ConversationTitle = conversation.Title,
            ConversationPlatformDisplay = conversation.PlatformDisplay,
            ConversationStartTimeDisplay = conversation.StartTimeDisplay,
            ConversationEndTimeDisplay = conversation.EndTimeDisplay,
            ConversationMessageCountDisplay = conversation.MessageCountDisplay,
            ConversationSourceCountDisplay = conversation.SourceCountDisplay,
            ConversationGapCountDisplay = conversation.GapCountDisplay,
            ConversationPriorityScoreDisplay = conversation.PriorityScoreDisplay,
            ConversationReconciliationStatus = conversation.ReconciliationStatus,
            ConversationReviewStatus = conversation.ReviewStatus
        };
    }

    public static ConversationInspectorViewModel FromMessage(
        ConversationListItemViewModel conversation,
        ConversationThreadMessageViewModel message)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(message);

        var sourceContext = message.SourceContext;

        return new ConversationInspectorViewModel("Safe source context loaded for the selected message.")
        {
            HasConversationSummary = true,
            ConversationTitle = conversation.Title,
            ConversationPlatformDisplay = conversation.PlatformDisplay,
            ConversationStartTimeDisplay = conversation.StartTimeDisplay,
            ConversationEndTimeDisplay = conversation.EndTimeDisplay,
            ConversationMessageCountDisplay = conversation.MessageCountDisplay,
            ConversationSourceCountDisplay = conversation.SourceCountDisplay,
            ConversationGapCountDisplay = conversation.GapCountDisplay,
            ConversationPriorityScoreDisplay = conversation.PriorityScoreDisplay,
            ConversationReconciliationStatus = conversation.ReconciliationStatus,
            ConversationReviewStatus = conversation.ReviewStatus,
            HasMessageContext = true,
            HasSourceContext = sourceContext is not null,
            MessageIdDisplay = message.MessageId,
            SelectedMessageTimestampDisplay = message.TimestampDisplay,
            DirectionDisplay = message.DirectionDisplay,
            SelectedMessageSenderDisplay = message.SenderDisplayLabel,
            RecipientDisplay = message.RecipientsDisplay,
            SourceImportIdDisplay = sourceContext?.SourceImportId ?? "-",
            SourceNameDisplay = sourceContext?.SourceName ?? "-",
            SourceTypeDisplay = sourceContext?.SourceType ?? "-",
            PlatformDisplay = string.IsNullOrWhiteSpace(sourceContext?.Platform) ? "-" : sourceContext.Platform!,
            OriginalFilenameDisplay = sourceContext?.OriginalFilename ?? "-",
            SourceArtifactIdDisplay = sourceContext?.SourceArtifactId ?? "-",
            ArtifactLocatorDisplay = sourceContext?.ArtifactLocator ?? "-",
            ProviderMessageIdDisplay = sourceContext?.ProviderMessageId ?? "-",
            SourceThreadIdDisplay = sourceContext?.SourceThreadId ?? "-",
            MessageHashPrefixDisplay = sourceContext?.MessageHashPrefix ?? "-"
        };
    }
}
