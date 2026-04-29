using DumpLens.Application.Identities;
using DumpLens.Normalization.Identities;

namespace DumpLens.Tests.Unit.Normalization.Identities;

public sealed class IdentityNormalizerTests
{
    private readonly IdentityNormalizer _normalizer = new();

    [Theory]
    [InlineData("8035551212", "+18035551212", "803-555-1212")]
    [InlineData("(803) 555-1212", "+18035551212", "803-555-1212")]
    [InlineData("+1 803 555 1212", "+18035551212", "803-555-1212")]
    public void Normalize_PhoneNumber_NormalizesCommonUsFormats(string rawValue, string expectedNormalizedValue, string expectedDisplayValue)
    {
        var result = _normalizer.Normalize(new IdentityNormalizeRequest
        {
            IdentityType = IdentityTypes.PhoneNumber,
            RawValue = rawValue
        });

        Assert.Equal(IdentityTypes.PhoneNumber, result.IdentityType);
        Assert.Equal(rawValue, result.RawValue);
        Assert.Equal(expectedNormalizedValue, result.NormalizedValue);
        Assert.Equal(expectedDisplayValue, result.DisplayValue);
        Assert.Equal(IdentityNormalizationConfidence.High, result.Confidence);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("555", IdentityNormalizeWarningCodes.InvalidPhoneNumber)]
    [InlineData("+44 20 7946 0958", IdentityNormalizeWarningCodes.AmbiguousPhoneNumber)]
    public void Normalize_PhoneNumber_ReturnsLowConfidenceWarningsForAmbiguousOrUnsupportedValues(string rawValue, string expectedWarningCode)
    {
        var result = _normalizer.Normalize(new IdentityNormalizeRequest
        {
            IdentityType = IdentityTypes.PhoneNumber,
            RawValue = rawValue
        });

        Assert.Equal(rawValue, result.RawValue);
        Assert.Equal(IdentityNormalizationConfidence.Low, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == expectedWarningCode);
    }

    [Theory]
    [InlineData("@Mike_170", "mike_170")]
    [InlineData("https://www.instagram.com/Mike_170/", "mike_170")]
    [InlineData("snap: mikegone", "mikegone")]
    public void Normalize_SocialHandle_ProducesStableLowercaseHandles(string rawValue, string expectedNormalizedValue)
    {
        var result = _normalizer.Normalize(new IdentityNormalizeRequest
        {
            IdentityType = IdentityTypes.SocialHandle,
            RawValue = rawValue
        });

        Assert.Equal(rawValue, result.RawValue);
        Assert.Equal(expectedNormalizedValue, result.NormalizedValue);
        Assert.Equal($"@{expectedNormalizedValue}", result.DisplayValue);
        Assert.DoesNotContain(result.Warnings, warning => warning.Code == IdentityNormalizeWarningCodes.InvalidHandle);
    }

    [Fact]
    public void Normalize_SocialHandle_ReturnsWarningWhenHandleContainsSpaces()
    {
        var result = _normalizer.Normalize(new IdentityNormalizeRequest
        {
            IdentityType = IdentityTypes.SocialHandle,
            RawValue = "bad handle with spaces"
        });

        Assert.Equal(IdentityNormalizationConfidence.Low, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == IdentityNormalizeWarningCodes.InvalidHandle);
    }

    [Theory]
    [InlineData("TEST@Example.COM", "test@example.com")]
    [InlineData("  TEST@Example.COM  ", "test@example.com")]
    public void Normalize_Email_LowercasesAndValidatesBasicShape(string rawValue, string expectedNormalizedValue)
    {
        var result = _normalizer.Normalize(new IdentityNormalizeRequest
        {
            IdentityType = IdentityTypes.Email,
            RawValue = rawValue
        });

        Assert.Equal(rawValue, result.RawValue);
        Assert.Equal(expectedNormalizedValue, result.NormalizedValue);
        Assert.Equal(expectedNormalizedValue, result.DisplayValue);
        Assert.Equal(IdentityNormalizationConfidence.High, result.Confidence);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Normalize_Email_ReturnsWarningForInvalidEmail()
    {
        var result = _normalizer.Normalize(new IdentityNormalizeRequest
        {
            IdentityType = IdentityTypes.Email,
            RawValue = "not-an-email"
        });

        Assert.Equal(IdentityNormalizationConfidence.Low, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == IdentityNormalizeWarningCodes.InvalidEmail);
    }

    [Theory]
    [InlineData(IdentityTypes.Nickname)]
    [InlineData(IdentityTypes.ContactName)]
    public void Normalize_NameTypes_TrimCollapseWhitespaceAndLowercaseNormalizedValue(string identityType)
    {
        var result = _normalizer.Normalize(new IdentityNormalizeRequest
        {
            IdentityType = identityType,
            RawValue = "  Lil   Mike  "
        });

        Assert.Equal("Lil Mike", result.DisplayValue);
        Assert.Equal("lil mike", result.NormalizedValue);
        Assert.Equal(IdentityNormalizationConfidence.Medium, result.Confidence);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(IdentityTypes.Nickname)]
    [InlineData(IdentityTypes.ContactName)]
    public void Normalize_NameTypes_ReturnWarningsForBlankValues(string identityType)
    {
        var result = _normalizer.Normalize(new IdentityNormalizeRequest
        {
            IdentityType = identityType,
            RawValue = "   "
        });

        Assert.Equal(string.Empty, result.DisplayValue);
        Assert.Equal(string.Empty, result.NormalizedValue);
        Assert.Equal(IdentityNormalizationConfidence.Unknown, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == IdentityNormalizeWarningCodes.EmptyValue);
        Assert.Contains(result.Warnings, warning => warning.Code == IdentityNormalizeWarningCodes.NormalizedValueEmpty);
    }

    [Fact]
    public void Normalize_UnknownType_PreservesValueWithUnknownConfidence()
    {
        var result = _normalizer.Normalize(new IdentityNormalizeRequest
        {
            IdentityType = IdentityTypes.Unknown,
            RawValue = "  Some Mixed Value  "
        });

        Assert.Equal("Some Mixed Value", result.DisplayValue);
        Assert.Equal("some mixed value", result.NormalizedValue);
        Assert.Equal(IdentityNormalizationConfidence.Unknown, result.Confidence);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Normalize_UnsupportedIdentityType_ReturnsWarning()
    {
        var result = _normalizer.Normalize(new IdentityNormalizeRequest
        {
            IdentityType = "pager_code",
            RawValue = "555-01"
        });

        Assert.Equal("pager_code", result.IdentityType);
        Assert.Equal(IdentityNormalizationConfidence.Unknown, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Code == IdentityNormalizeWarningCodes.UnsupportedIdentityType);
    }

    [Fact]
    public void Normalize_UsesProvidedDisplayValueWhenPresent()
    {
        var result = _normalizer.Normalize(new IdentityNormalizeRequest
        {
            IdentityType = IdentityTypes.Email,
            RawValue = "raw@example.com",
            DisplayValue = "Display@Example.com"
        });

        Assert.Equal("raw@example.com", result.RawValue);
        Assert.Equal("display@example.com", result.DisplayValue);
        Assert.Equal("display@example.com", result.NormalizedValue);
    }
}
