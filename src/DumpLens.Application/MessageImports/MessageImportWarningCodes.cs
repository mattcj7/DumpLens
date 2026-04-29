namespace DumpLens.Application.MessageImports;

public static class MessageImportWarningCodes
{
    public const string MissingSourceImport = "missing_source_import";
    public const string SourceFileNotFound = "source_file_not_found";
    public const string MissingRequiredMapping = "missing_required_mapping";
    public const string MissingTimestamp = "missing_timestamp";
    public const string InvalidTimestamp = "invalid_timestamp";
    public const string MissingSender = "missing_sender";
    public const string MissingRecipient = "missing_recipient";
    public const string MissingMessageBody = "missing_message_body";
    public const string AmbiguousSenderIdentity = "ambiguous_sender_identity";
    public const string AmbiguousRecipientIdentity = "ambiguous_recipient_identity";
    public const string InvalidSenderIdentity = "invalid_sender_identity";
    public const string InvalidRecipientIdentity = "invalid_recipient_identity";
    public const string RowParseWarning = "row_parse_warning";
    public const string RowImportFailed = "row_import_failed";
    public const string UnsupportedSourceKind = "unsupported_source_kind";
    public const string WorksheetNotFound = "worksheet_not_found";
    public const string DuplicateProviderMessageId = "duplicate_provider_message_id";
    public const string AttachmentNotPersisted = "attachment_not_persisted";
    public const string MultipleRecipientsSplit = "multiple_recipients_split";
    public const string UnknownPlatform = "unknown_platform";
}
