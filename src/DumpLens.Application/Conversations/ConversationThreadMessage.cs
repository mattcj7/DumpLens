namespace DumpLens.Application.Conversations;

public sealed record ConversationThreadMessage
{
    public required string MessageId { get; init; }

    public DateTimeOffset? EventTimeUtc { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public string? Direction { get; init; }

    public required string SenderDisplayLabel { get; init; }

    public IReadOnlyList<string> RecipientDisplayLabels { get; init; } = [];

    public string? MessageBody { get; init; }

    public string? Platform { get; init; }

    public required string DeletedStatus { get; init; }

    public bool HasSourceReference { get; init; }

    public ConversationSourceContext? SourceContext { get; init; }
}
