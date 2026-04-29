using DumpLens.Application.Timestamps;
using DumpLens.Normalization.Timestamps;

namespace DumpLens.Tests.Unit.Normalization.Timestamps;

public sealed class TimestampNormalizerTests
{
    private readonly TimestampNormalizer _normalizer = new();

    [Fact]
    public void Normalize_IsoUtcTimestamp_ParsesToExpectedUtcValue()
    {
        const string rawValue = "2026-04-28T19:30:00Z";

        var result = _normalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = rawValue
        });

        Assert.Equal(rawValue, result.OriginalValue);
        Assert.Equal(new DateTimeOffset(2026, 4, 28, 19, 30, 0, TimeSpan.Zero), result.NormalizedUtc);
        Assert.Equal(new DateTime(2026, 4, 28, 19, 30, 0), result.LocalDateTime);
        Assert.Equal(TimestampNormalizationConfidence.High, result.Confidence);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Normalize_OffsetTimestamp_ParsesToExpectedUtcValue()
    {
        const string rawValue = "2026-04-28T19:30:00-04:00";

        var result = _normalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = rawValue,
            TimezoneAssumption = "Eastern Standard Time"
        });

        Assert.Equal(rawValue, result.OriginalValue);
        Assert.Equal("Eastern Standard Time", result.TimezoneAssumption);
        Assert.Equal(new DateTimeOffset(2026, 4, 28, 23, 30, 0, TimeSpan.Zero), result.NormalizedUtc);
        Assert.Equal(new DateTime(2026, 4, 28, 19, 30, 0), result.LocalDateTime);
        Assert.Equal(TimestampNormalizationConfidence.High, result.Confidence);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("04/28/2026 7:30 PM")]
    [InlineData("2026-04-28 19:30:00")]
    public void Normalize_TimezoneLessTimestamp_UsesProvidedWindowsTimezone(string rawValue)
    {
        var result = _normalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = rawValue,
            TimezoneAssumption = "Eastern Standard Time"
        });

        Assert.Equal(new DateTimeOffset(2026, 4, 28, 23, 30, 0, TimeSpan.Zero), result.NormalizedUtc);
        Assert.Equal(new DateTime(2026, 4, 28, 19, 30, 0), result.LocalDateTime);
        Assert.Equal("Eastern Standard Time", result.ResolvedTimezoneId);
        Assert.Equal(TimestampNormalizationConfidence.High, result.Confidence);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Normalize_DateOnlyTimestamp_AssumesMidnightAndReturnsWarning()
    {
        const string rawValue = "2026-04-28";

        var result = _normalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = rawValue,
            TimezoneAssumption = "Eastern Standard Time"
        });

        Assert.Equal(new DateTimeOffset(2026, 4, 28, 4, 0, 0, TimeSpan.Zero), result.NormalizedUtc);
        Assert.Equal(new DateTime(2026, 4, 28, 0, 0, 0), result.LocalDateTime);
        Assert.Equal(TimestampNormalizationConfidence.Medium, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == TimestampNormalizeWarningCodes.DateOnlyAssumedMidnight);
    }

    [Fact]
    public void Normalize_UnixSeconds_ParsesAsUtcAndReturnsDetectionWarning()
    {
        const string rawValue = "1777404600";

        var result = _normalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = rawValue
        });

        Assert.Equal(new DateTimeOffset(2026, 4, 28, 19, 30, 0, TimeSpan.Zero), result.NormalizedUtc);
        Assert.Equal(new DateTime(2026, 4, 28, 19, 30, 0), result.LocalDateTime);
        Assert.Equal("UTC", result.ResolvedTimezoneId);
        Assert.Equal(TimestampNormalizationConfidence.Medium, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == TimestampNormalizeWarningCodes.UnixTimestampDetected);
    }

    [Fact]
    public void Normalize_UnixMilliseconds_ParsesAsUtcAndReturnsDetectionWarning()
    {
        const string rawValue = "1777404600000";

        var result = _normalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = rawValue
        });

        Assert.Equal(new DateTimeOffset(2026, 4, 28, 19, 30, 0, TimeSpan.Zero), result.NormalizedUtc);
        Assert.Equal(new DateTime(2026, 4, 28, 19, 30, 0), result.LocalDateTime);
        Assert.Equal(TimestampNormalizationConfidence.Medium, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == TimestampNormalizeWarningCodes.UnixTimestampDetected);
    }

    [Fact]
    public void Normalize_TwoDigitYearUsTimestamp_IsSupported()
    {
        const string rawValue = "04/28/26 7:30 PM";

        var result = _normalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = rawValue,
            TimezoneAssumption = "Eastern Standard Time"
        });

        Assert.Equal(new DateTimeOffset(2026, 4, 28, 23, 30, 0, TimeSpan.Zero), result.NormalizedUtc);
        Assert.Equal(new DateTime(2026, 4, 28, 19, 30, 0), result.LocalDateTime);
        Assert.Equal(TimestampNormalizationConfidence.High, result.Confidence);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Normalize_AmbiguousLocalTimestamp_ReturnsWarningAndChoosesStandardTimeDeterministically()
    {
        const string rawValue = "11/01/2026 1:30 AM";

        var result = _normalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = rawValue,
            TimezoneAssumption = "Eastern Standard Time"
        });

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), result.NormalizedUtc);
        Assert.Equal(new DateTime(2026, 11, 1, 1, 30, 0), result.LocalDateTime);
        Assert.Equal(TimestampNormalizationConfidence.Medium, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == TimestampNormalizeWarningCodes.AmbiguousTimestamp);
    }

    [Fact]
    public void Normalize_MissingTimezoneAssumption_ReturnsWarningAndLowConfidence()
    {
        const string rawValue = "2026-04-28 19:30:00";

        var result = _normalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = rawValue
        });

        Assert.Null(result.NormalizedUtc);
        Assert.Equal(new DateTime(2026, 4, 28, 19, 30, 0), result.LocalDateTime);
        Assert.Equal(TimestampNormalizationConfidence.Low, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == TimestampNormalizeWarningCodes.MissingTimezoneAssumption);
    }

    [Fact]
    public void Normalize_InvalidTimezoneAssumption_ReturnsWarningAndLowConfidence()
    {
        const string rawValue = "2026-04-28 19:30:00";

        var result = _normalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = rawValue,
            TimezoneAssumption = "Mars Standard Time"
        });

        Assert.Null(result.NormalizedUtc);
        Assert.Equal(new DateTime(2026, 4, 28, 19, 30, 0), result.LocalDateTime);
        Assert.Equal(TimestampNormalizationConfidence.Low, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == TimestampNormalizeWarningCodes.InvalidTimezoneAssumption);
    }

    [Fact]
    public void Normalize_InvalidTimestamp_ReturnsWarningAndPreservesOriginalValue()
    {
        const string rawValue = "not a date";

        var result = _normalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = rawValue,
            TimezoneAssumption = "Eastern Standard Time"
        });

        Assert.Equal(rawValue, result.OriginalValue);
        Assert.Null(result.NormalizedUtc);
        Assert.Null(result.LocalDateTime);
        Assert.Equal(TimestampNormalizationConfidence.Unknown, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == TimestampNormalizeWarningCodes.InvalidTimestamp);
        Assert.Contains(result.Warnings, warning => warning.Code == TimestampNormalizeWarningCodes.UnsupportedTimestampFormat);
    }

    [Fact]
    public void Normalize_EmptyTimestamp_ReturnsEmptyAndNormalizedValueWarnings()
    {
        var result = _normalizer.Normalize(new TimestampNormalizeRequest
        {
            OriginalValue = "   ",
            TimezoneAssumption = "Eastern Standard Time"
        });

        Assert.Null(result.NormalizedUtc);
        Assert.Null(result.LocalDateTime);
        Assert.Equal(TimestampNormalizationConfidence.Unknown, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == TimestampNormalizeWarningCodes.EmptyValue);
        Assert.Contains(result.Warnings, warning => warning.Code == TimestampNormalizeWarningCodes.NormalizedValueEmpty);
    }
}
