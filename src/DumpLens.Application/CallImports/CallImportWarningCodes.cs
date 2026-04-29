namespace DumpLens.Application.CallImports;

public static class CallImportWarningCodes
{
    public const string MissingSourceImport = "missing_source_import";
    public const string SourceFileNotFound = "source_file_not_found";
    public const string MissingRequiredMapping = "missing_required_mapping";
    public const string MissingTimestamp = "missing_timestamp";
    public const string InvalidTimestamp = "invalid_timestamp";
    public const string MissingCaller = "missing_caller";
    public const string MissingCallee = "missing_callee";
    public const string AmbiguousCallerIdentity = "ambiguous_caller_identity";
    public const string AmbiguousCalleeIdentity = "ambiguous_callee_identity";
    public const string InvalidCallerIdentity = "invalid_caller_identity";
    public const string InvalidCalleeIdentity = "invalid_callee_identity";
    public const string MissingDuration = "missing_duration";
    public const string InvalidDuration = "invalid_duration";
    public const string RowParseWarning = "row_parse_warning";
    public const string RowImportFailed = "row_import_failed";
    public const string UnsupportedSourceKind = "unsupported_source_kind";
    public const string WorksheetNotFound = "worksheet_not_found";
    public const string UnknownPlatformOrCarrier = "unknown_platform_or_carrier";
}
