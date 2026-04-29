namespace DumpLens.Application.Timestamps;

public static class TimestampNormalizeWarningCodes
{
    public const string EmptyValue = "empty_value";
    public const string InvalidTimestamp = "invalid_timestamp";
    public const string AmbiguousTimestamp = "ambiguous_timestamp";
    public const string MissingTimezoneAssumption = "missing_timezone_assumption";
    public const string InvalidTimezoneAssumption = "invalid_timezone_assumption";
    public const string DateOnlyAssumedMidnight = "date_only_assumed_midnight";
    public const string UnixTimestampDetected = "unix_timestamp_detected";
    public const string UnsupportedTimestampFormat = "unsupported_timestamp_format";
    public const string NormalizedValueEmpty = "normalized_value_empty";
}
