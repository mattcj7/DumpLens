using System.Globalization;
using System.Text.RegularExpressions;
using DumpLens.Application.Timestamps;

namespace DumpLens.Normalization.Timestamps;

public sealed partial class TimestampNormalizer : ITimestampNormalizer
{
    private static readonly string[] OffsetTimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm.FFFFFFFK",
        "yyyy-MM-dd HH:mm:ssK",
        "yyyy-MM-dd HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd HH:mmK",
        "yyyy-MM-dd HH:mm.FFFFFFFK"
    ];

    private static readonly string[] LocalTimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm",
        "M/d/yyyy h:mm tt",
        "MM/dd/yyyy h:mm tt",
        "M/d/yy h:mm tt",
        "MM/dd/yy h:mm tt",
        "M/d/yyyy H:mm",
        "MM/dd/yyyy H:mm",
        "M/d/yy H:mm",
        "MM/dd/yy H:mm"
    ];

    private static readonly string[] DateOnlyFormats =
    [
        "yyyy-MM-dd",
        "M/d/yyyy",
        "MM/dd/yyyy",
        "M/d/yy",
        "MM/dd/yy"
    ];

    private static readonly DateTimeOffset MinimumUnixTimestampUtc = new(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MaximumUnixTimestampUtc = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public TimestampNormalizeResult Normalize(TimestampNormalizeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var originalValue = request.OriginalValue;
        var timezoneAssumption = request.TimezoneAssumption;
        var trimmedValue = request.OriginalValue?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedValue))
        {
            return CreateResult(
                originalValue,
                timezoneAssumption,
                resolvedTimezoneId: null,
                normalizedUtc: null,
                localDateTime: null,
                confidence: TimestampNormalizationConfidence.Unknown,
                CreateWarnings(
                    (TimestampNormalizeWarningCodes.EmptyValue, "The timestamp value is empty."),
                    (TimestampNormalizeWarningCodes.NormalizedValueEmpty, "Normalization did not produce a UTC timestamp.")));
        }

        if (HasExplicitOffset(trimmedValue))
        {
            return NormalizeExplicitOffsetTimestamp(originalValue, timezoneAssumption, trimmedValue);
        }

        if (TryParseUnixTimestamp(trimmedValue, out var unixUtcValue))
        {
            return CreateResult(
                originalValue,
                timezoneAssumption,
                resolvedTimezoneId: TimeZoneInfo.Utc.Id,
                normalizedUtc: unixUtcValue,
                localDateTime: unixUtcValue.UtcDateTime,
                confidence: TimestampNormalizationConfidence.Medium,
                CreateWarnings((TimestampNormalizeWarningCodes.UnixTimestampDetected, "The numeric timestamp was interpreted as a Unix timestamp.")));
        }

        if (TryParseDateOnly(trimmedValue, out var dateOnlyLocal))
        {
            return NormalizeLocalTimestamp(
                originalValue,
                timezoneAssumption,
                localDateTime: dateOnlyLocal,
                isDateOnly: true);
        }

        if (TryParseLocalTimestamp(trimmedValue, out var localDateTime))
        {
            return NormalizeLocalTimestamp(
                originalValue,
                timezoneAssumption,
                localDateTime,
                isDateOnly: false);
        }

        var warnings = MatchesSupportedTimestampShape(trimmedValue)
            ? CreateWarnings((TimestampNormalizeWarningCodes.InvalidTimestamp, "The timestamp matches a supported shape but is not a valid date/time value."))
            : CreateWarnings(
                (TimestampNormalizeWarningCodes.InvalidTimestamp, "The timestamp could not be parsed."),
                (TimestampNormalizeWarningCodes.UnsupportedTimestampFormat, "The timestamp format is not supported."));

        return CreateResult(
            originalValue,
            timezoneAssumption,
            resolvedTimezoneId: null,
            normalizedUtc: null,
            localDateTime: null,
            confidence: MatchesSupportedTimestampShape(trimmedValue)
                ? TimestampNormalizationConfidence.Low
                : TimestampNormalizationConfidence.Unknown,
            warnings);
    }

    private static TimestampNormalizeResult NormalizeExplicitOffsetTimestamp(
        string? originalValue,
        string? timezoneAssumption,
        string trimmedValue)
    {
        if (!DateTimeOffset.TryParseExact(
                trimmedValue,
                OffsetTimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var offsetValue))
        {
            return CreateResult(
                originalValue,
                timezoneAssumption,
                resolvedTimezoneId: null,
                normalizedUtc: null,
                localDateTime: null,
                confidence: TimestampNormalizationConfidence.Low,
                CreateWarnings((TimestampNormalizeWarningCodes.InvalidTimestamp, "The timestamp includes timezone information but could not be parsed.")));
        }

        return CreateResult(
            originalValue,
            timezoneAssumption,
            resolvedTimezoneId: null,
            normalizedUtc: offsetValue.ToUniversalTime(),
            localDateTime: offsetValue.DateTime,
            confidence: TimestampNormalizationConfidence.High,
            []);
    }

    private static TimestampNormalizeResult NormalizeLocalTimestamp(
        string? originalValue,
        string? timezoneAssumption,
        DateTime localDateTime,
        bool isDateOnly)
    {
        var warnings = new List<TimestampNormalizeWarning>();
        if (isDateOnly)
        {
            warnings.Add(CreateWarning(
                TimestampNormalizeWarningCodes.DateOnlyAssumedMidnight,
                "The date-only timestamp was normalized using midnight in the assumed timezone."));
        }

        var normalizedTimezoneAssumption = timezoneAssumption?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTimezoneAssumption))
        {
            warnings.Add(CreateWarning(
                TimestampNormalizeWarningCodes.MissingTimezoneAssumption,
                "A timezone assumption is required when the source timestamp does not include timezone information."));

            return CreateResult(
                originalValue,
                timezoneAssumption,
                resolvedTimezoneId: null,
                normalizedUtc: null,
                localDateTime: localDateTime,
                confidence: TimestampNormalizationConfidence.Low,
                warnings);
        }

        TimeZoneInfo timezone;
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(normalizedTimezoneAssumption);
        }
        catch (TimeZoneNotFoundException)
        {
            warnings.Add(CreateWarning(
                TimestampNormalizeWarningCodes.InvalidTimezoneAssumption,
                "The timezone assumption is not a valid system timezone ID."));

            return CreateResult(
                originalValue,
                timezoneAssumption,
                resolvedTimezoneId: null,
                normalizedUtc: null,
                localDateTime: localDateTime,
                confidence: TimestampNormalizationConfidence.Low,
                warnings);
        }
        catch (InvalidTimeZoneException)
        {
            warnings.Add(CreateWarning(
                TimestampNormalizeWarningCodes.InvalidTimezoneAssumption,
                "The timezone assumption could not be resolved."));

            return CreateResult(
                originalValue,
                timezoneAssumption,
                resolvedTimezoneId: null,
                normalizedUtc: null,
                localDateTime: localDateTime,
                confidence: TimestampNormalizationConfidence.Low,
                warnings);
        }

        if (timezone.IsInvalidTime(localDateTime))
        {
            warnings.Add(CreateWarning(
                TimestampNormalizeWarningCodes.InvalidTimestamp,
                "The local timestamp is not valid in the assumed timezone."));

            return CreateResult(
                originalValue,
                timezoneAssumption,
                resolvedTimezoneId: timezone.Id,
                normalizedUtc: null,
                localDateTime: localDateTime,
                confidence: TimestampNormalizationConfidence.Low,
                warnings);
        }

        var confidence = isDateOnly
            ? TimestampNormalizationConfidence.Medium
            : TimestampNormalizationConfidence.High;

        TimeSpan localOffset;
        if (timezone.IsAmbiguousTime(localDateTime))
        {
            warnings.Add(CreateWarning(
                TimestampNormalizeWarningCodes.AmbiguousTimestamp,
                "The local timestamp is ambiguous in the assumed timezone. Standard time was chosen deterministically."));

            localOffset = ResolveAmbiguousOffset(timezone, localDateTime);
            confidence = TimestampNormalizationConfidence.Medium;
        }
        else
        {
            localOffset = timezone.GetUtcOffset(localDateTime);
        }

        var normalizedUtc = new DateTimeOffset(localDateTime, localOffset).ToUniversalTime();
        return CreateResult(
            originalValue,
            timezoneAssumption,
            resolvedTimezoneId: timezone.Id,
            normalizedUtc: normalizedUtc,
            localDateTime: localDateTime,
            confidence: confidence,
            warnings);
    }

    private static bool HasExplicitOffset(string value)
    {
        return ExplicitOffsetRegex().IsMatch(value);
    }

    private static bool TryParseUnixTimestamp(string value, out DateTimeOffset utcValue)
    {
        utcValue = default;
        if (!DigitsOnlyRegex().IsMatch(value) ||
            !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var numericValue))
        {
            return false;
        }

        DateTimeOffset candidate;
        if (value.Length == 10)
        {
            candidate = DateTimeOffset.FromUnixTimeSeconds(numericValue);
        }
        else if (value.Length == 13)
        {
            candidate = DateTimeOffset.FromUnixTimeMilliseconds(numericValue);
        }
        else
        {
            return false;
        }

        if (candidate < MinimumUnixTimestampUtc || candidate > MaximumUnixTimestampUtc)
        {
            return false;
        }

        utcValue = candidate.ToUniversalTime();
        return true;
    }

    private static bool TryParseLocalTimestamp(string value, out DateTime localDateTime)
    {
        return DateTime.TryParseExact(
            value,
            LocalTimestampFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out localDateTime);
    }

    private static bool TryParseDateOnly(string value, out DateTime localDateTime)
    {
        if (!DateTime.TryParseExact(
                value,
                DateOnlyFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out localDateTime))
        {
            return false;
        }

        localDateTime = DateTime.SpecifyKind(localDateTime.Date, DateTimeKind.Unspecified);
        return true;
    }

    private static bool MatchesSupportedTimestampShape(string value)
    {
        return SupportedTimestampShapeRegex().IsMatch(value) || DigitsOnlyRegex().IsMatch(value);
    }

    private static TimeSpan ResolveAmbiguousOffset(TimeZoneInfo timezone, DateTime localDateTime)
    {
        var ambiguousOffsets = timezone.GetAmbiguousTimeOffsets(localDateTime);
        foreach (var ambiguousOffset in ambiguousOffsets)
        {
            if (ambiguousOffset == timezone.BaseUtcOffset)
            {
                return ambiguousOffset;
            }
        }

        return ambiguousOffsets.OrderBy(static offset => offset).First();
    }

    private static TimestampNormalizeResult CreateResult(
        string? originalValue,
        string? timezoneAssumption,
        string? resolvedTimezoneId,
        DateTimeOffset? normalizedUtc,
        DateTime? localDateTime,
        string confidence,
        IEnumerable<TimestampNormalizeWarning> warnings)
    {
        return new TimestampNormalizeResult
        {
            OriginalValue = originalValue,
            TimezoneAssumption = timezoneAssumption,
            ResolvedTimezoneId = resolvedTimezoneId,
            NormalizedUtc = normalizedUtc,
            LocalDateTime = localDateTime,
            Confidence = confidence,
            Warnings = warnings.ToArray()
        };
    }

    private static TimestampNormalizeWarning[] CreateWarnings(params (string Code, string Message)[] warningPairs)
    {
        return warningPairs
            .Select(static pair => CreateWarning(pair.Code, pair.Message))
            .ToArray();
    }

    private static TimestampNormalizeWarning CreateWarning(string code, string message)
    {
        return new TimestampNormalizeWarning
        {
            Code = code,
            Message = message
        };
    }

    [GeneratedRegex(@"(?:Z|[+\-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitOffsetRegex();

    [GeneratedRegex(@"^\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsOnlyRegex();

    [GeneratedRegex(@"^(?:\d{4}-\d{2}-\d{2}(?:[ T]\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?)?(?:Z|[+\-]\d{2}:\d{2})?|\d{1,2}/\d{1,2}/\d{2,4}(?:\s+\d{1,2}:\d{2}(?:\s?[APap][Mm])?)?)$", RegexOptions.CultureInvariant)]
    private static partial Regex SupportedTimestampShapeRegex();
}
