using System.Globalization;
using DumpLens.Application.Conversations;

namespace DumpLens.App.ViewModels;

public sealed class ConversationThreadMessageViewModel
{
    public ConversationThreadMessageViewModel(ConversationThreadMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        MessageId = message.MessageId;
        EventTimeUtc = message.EventTimeUtc;
        CreatedAtUtc = message.CreatedAtUtc;
        Direction = message.Direction;
        SenderDisplayLabel = message.SenderDisplayLabel;
        RecipientDisplayLabels = message.RecipientDisplayLabels;
        MessageBody = message.MessageBody ?? string.Empty;
        Platform = message.Platform;
        DeletedStatus = message.DeletedStatus;
        HasSourceReference = message.HasSourceReference;
        SourceContext = message.SourceContext;
        TimestampDisplay = FormatUtc(message.EventTimeUtc ?? message.CreatedAtUtc);
        DirectionDisplay = string.IsNullOrWhiteSpace(message.Direction) ? "-" : message.Direction!;
        RecipientsDisplay = message.RecipientDisplayLabels.Count == 0
            ? "Unknown recipient"
            : string.Join(", ", message.RecipientDisplayLabels);
        PlatformDisplay = string.IsNullOrWhiteSpace(message.Platform) ? "-" : message.Platform!;
        SourceReferenceIndicator = message.HasSourceReference ? "Source linked" : "Source unavailable";
    }

    public DateTimeOffset CreatedAtUtc { get; }

    public string DeletedStatus { get; }

    public string DirectionDisplay { get; }

    public DateTimeOffset? EventTimeUtc { get; }

    public bool HasSourceReference { get; }

    public string MessageBody { get; }

    public string MessageId { get; }

    public string PlatformDisplay { get; }

    public IReadOnlyList<string> RecipientDisplayLabels { get; }

    public string RecipientsDisplay { get; }

    public string SenderDisplayLabel { get; }

    public ConversationSourceContext? SourceContext { get; }

    public string SourceReferenceIndicator { get; }

    public string TimestampDisplay { get; }

    private string? Direction { get; }

    private string? Platform { get; }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    }
}
