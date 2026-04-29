namespace DumpLens.Application.Imports;

public static class ImportFieldNames
{
    public const string Timestamp = "timestamp";
    public const string Sender = "sender";
    public const string Recipient = "recipient";
    public const string MessageBody = "message_body";
    public const string Platform = "platform";
    public const string Direction = "direction";
    public const string ThreadId = "thread_id";
    public const string MessageId = "message_id";
    public const string Attachment = "attachment";
    public const string Caller = "caller";
    public const string Callee = "callee";
    public const string Duration = "duration";
    public const string CallType = "call_type";

    public static readonly IReadOnlyList<string> All =
    [
        Timestamp,
        Sender,
        Recipient,
        MessageBody,
        Platform,
        Direction,
        ThreadId,
        MessageId,
        Attachment,
        Caller,
        Callee,
        Duration,
        CallType
    ];
}
