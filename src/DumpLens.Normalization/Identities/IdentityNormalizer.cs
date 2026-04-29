using System.Text;
using System.Text.RegularExpressions;
using DumpLens.Application.Identities;

namespace DumpLens.Normalization.Identities;

public sealed partial class IdentityNormalizer : IIdentityNormalizer
{
    private static readonly HashSet<string> PlaceholderNameValues =
    [
        "n/a",
        "na",
        "none",
        "null",
        "unknown",
        "unk",
        "-"
    ];

    public IdentityNormalizeResult Normalize(IdentityNormalizeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdentityType);

        var identityType = CanonicalizeIdentityType(request.IdentityType);
        var rawValue = request.RawValue;
        var trimmedRawValue = request.RawValue?.Trim() ?? string.Empty;
        var displaySeed = string.IsNullOrWhiteSpace(request.DisplayValue)
            ? trimmedRawValue
            : request.DisplayValue!.Trim();

        return identityType switch
        {
            IdentityTypes.PhoneNumber => NormalizePhoneNumber(identityType, rawValue, displaySeed),
            IdentityTypes.SocialHandle => NormalizeSocialHandle(identityType, rawValue, displaySeed),
            IdentityTypes.Email => NormalizeEmail(identityType, rawValue, displaySeed),
            IdentityTypes.Nickname => NormalizeName(identityType, rawValue, displaySeed),
            IdentityTypes.ContactName => NormalizeName(identityType, rawValue, displaySeed),
            IdentityTypes.Unknown => NormalizeUnknown(identityType, rawValue, displaySeed),
            _ => NormalizeUnsupportedIdentityType(identityType, rawValue, displaySeed)
        };
    }

    private static IdentityNormalizeResult NormalizePhoneNumber(
        string identityType,
        string? rawValue,
        string displaySeed)
    {
        var warnings = new List<IdentityNormalizeWarning>();
        if (TryCreateEmptyResult(identityType, rawValue, displaySeed, warnings, out var emptyResult))
        {
            return emptyResult;
        }

        var digits = ExtractDigits(displaySeed);
        if (digits.Length == 10)
        {
            return CreateResult(
                identityType,
                rawValue,
                FormatUsPhoneNumber(digits),
                $"+1{digits}",
                IdentityNormalizationConfidence.High,
                warnings);
        }

        if (digits.Length == 11 && digits[0] == '1')
        {
            var nationalNumber = digits[1..];
            return CreateResult(
                identityType,
                rawValue,
                FormatUsPhoneNumber(nationalNumber),
                $"+1{nationalNumber}",
                IdentityNormalizationConfidence.High,
                warnings);
        }

        if (displaySeed.StartsWith('+'))
        {
            var compactInternational = CompactInternationalPhone(displaySeed);
            if (!string.IsNullOrEmpty(compactInternational))
            {
                warnings.Add(CreateWarning(
                    IdentityNormalizeWarningCodes.AmbiguousPhoneNumber,
                    "The phone number was preserved because only practical US normalization is supported in this ticket."));

                return CreateResult(
                    identityType,
                    rawValue,
                    displaySeed,
                    compactInternational,
                    IdentityNormalizationConfidence.Low,
                    warnings);
            }
        }

        warnings.Add(CreateWarning(
            digits.Length is > 0 and < 10 or > 11
                ? IdentityNormalizeWarningCodes.InvalidPhoneNumber
                : IdentityNormalizeWarningCodes.AmbiguousPhoneNumber,
            digits.Length is > 0 and < 10 or > 11
                ? "The phone number does not fit the supported US normalization rules."
                : "The phone number could not be confidently normalized as a US number."));

        return CreateResult(
            identityType,
            rawValue,
            displaySeed,
            displaySeed,
            IdentityNormalizationConfidence.Low,
            warnings);
    }

    private static IdentityNormalizeResult NormalizeSocialHandle(
        string identityType,
        string? rawValue,
        string displaySeed)
    {
        var warnings = new List<IdentityNormalizeWarning>();
        if (TryCreateEmptyResult(identityType, rawValue, displaySeed, warnings, out var emptyResult))
        {
            return emptyResult;
        }

        if (TryExtractHandleFromUrl(displaySeed, out var urlHandle, out var ambiguousUrl))
        {
            return BuildHandleResult(
                identityType,
                rawValue,
                urlHandle!,
                IdentityNormalizationConfidence.Medium,
                warnings);
        }

        if (ambiguousUrl)
        {
            warnings.Add(CreateWarning(
                IdentityNormalizeWarningCodes.AmbiguousHandle,
                "The social handle URL could not be extracted unambiguously."));

            return CreateResult(
                identityType,
                rawValue,
                displaySeed,
                displaySeed.ToLowerInvariant(),
                IdentityNormalizationConfidence.Low,
                warnings);
        }

        if (TryExtractHandleFromPrefix(displaySeed, out var prefixedHandle))
        {
            return BuildHandleResult(
                identityType,
                rawValue,
                prefixedHandle!,
                IdentityNormalizationConfidence.Medium,
                warnings);
        }

        if (displaySeed.Contains(' '))
        {
            warnings.Add(CreateWarning(
                IdentityNormalizeWarningCodes.InvalidHandle,
                "The social handle contains spaces and could not be normalized with confidence."));

            return CreateResult(
                identityType,
                rawValue,
                displaySeed,
                displaySeed.ToLowerInvariant(),
                IdentityNormalizationConfidence.Low,
                warnings);
        }

        var directHandle = displaySeed.TrimStart('@');
        if (!IsValidDirectHandle(directHandle))
        {
            warnings.Add(CreateWarning(
                IdentityNormalizeWarningCodes.InvalidHandle,
                "The social handle contains unsupported characters."));

            return CreateResult(
                identityType,
                rawValue,
                displaySeed,
                directHandle.ToLowerInvariant(),
                IdentityNormalizationConfidence.Low,
                warnings);
        }

        return BuildHandleResult(
            identityType,
            rawValue,
            directHandle,
            IdentityNormalizationConfidence.High,
            warnings);
    }

    private static IdentityNormalizeResult NormalizeEmail(
        string identityType,
        string? rawValue,
        string displaySeed)
    {
        var warnings = new List<IdentityNormalizeWarning>();
        if (TryCreateEmptyResult(identityType, rawValue, displaySeed, warnings, out var emptyResult))
        {
            return emptyResult;
        }

        var normalizedValue = displaySeed.ToLowerInvariant();
        if (!IsValidBasicEmail(normalizedValue))
        {
            warnings.Add(CreateWarning(
                IdentityNormalizeWarningCodes.InvalidEmail,
                "The email address does not match the expected local@domain.tld shape."));

            return CreateResult(
                identityType,
                rawValue,
                displaySeed,
                normalizedValue,
                IdentityNormalizationConfidence.Low,
                warnings);
        }

        return CreateResult(
            identityType,
            rawValue,
            normalizedValue,
            normalizedValue,
            IdentityNormalizationConfidence.High,
            warnings);
    }

    private static IdentityNormalizeResult NormalizeName(
        string identityType,
        string? rawValue,
        string displaySeed)
    {
        var warnings = new List<IdentityNormalizeWarning>();
        var collapsedDisplayValue = CollapseWhitespace(displaySeed);
        if (TryCreateEmptyResult(identityType, rawValue, collapsedDisplayValue, warnings, out var emptyResult))
        {
            return emptyResult;
        }

        if (PlaceholderNameValues.Contains(collapsedDisplayValue.ToLowerInvariant()))
        {
            warnings.Add(CreateWarning(
                IdentityNormalizeWarningCodes.NormalizedValueEmpty,
                "The name value is too generic to normalize into a stable matching value."));

            return CreateResult(
                identityType,
                rawValue,
                collapsedDisplayValue,
                string.Empty,
                IdentityNormalizationConfidence.Low,
                warnings);
        }

        return CreateResult(
            identityType,
            rawValue,
            collapsedDisplayValue,
            collapsedDisplayValue.ToLowerInvariant(),
            IdentityNormalizationConfidence.Medium,
            warnings);
    }

    private static IdentityNormalizeResult NormalizeUnknown(
        string identityType,
        string? rawValue,
        string displaySeed)
    {
        var warnings = new List<IdentityNormalizeWarning>();
        var collapsedDisplayValue = CollapseWhitespace(displaySeed);
        if (TryCreateEmptyResult(identityType, rawValue, collapsedDisplayValue, warnings, out var emptyResult))
        {
            return emptyResult;
        }

        return CreateResult(
            identityType,
            rawValue,
            collapsedDisplayValue,
            collapsedDisplayValue.ToLowerInvariant(),
            IdentityNormalizationConfidence.Unknown,
            warnings);
    }

    private static IdentityNormalizeResult NormalizeUnsupportedIdentityType(
        string identityType,
        string? rawValue,
        string displaySeed)
    {
        var warnings = new List<IdentityNormalizeWarning>
        {
            CreateWarning(
                IdentityNormalizeWarningCodes.UnsupportedIdentityType,
                "The identity type is not supported by this normalizer.")
        };

        var collapsedDisplayValue = CollapseWhitespace(displaySeed);
        if (string.IsNullOrEmpty(collapsedDisplayValue))
        {
            warnings.Add(CreateWarning(
                IdentityNormalizeWarningCodes.EmptyValue,
                "The identity value is empty."));
            warnings.Add(CreateWarning(
                IdentityNormalizeWarningCodes.NormalizedValueEmpty,
                "Normalization did not produce a stable value."));
        }

        return CreateResult(
            identityType,
            rawValue,
            collapsedDisplayValue,
            collapsedDisplayValue.ToLowerInvariant(),
            IdentityNormalizationConfidence.Unknown,
            warnings);
    }

    private static IdentityNormalizeResult BuildHandleResult(
        string identityType,
        string? rawValue,
        string extractedHandle,
        string confidence,
        IReadOnlyCollection<IdentityNormalizeWarning> warnings)
    {
        var normalizedValue = extractedHandle.Trim().TrimStart('@').ToLowerInvariant();
        var displayValue = string.Concat("@", normalizedValue);

        return CreateResult(
            identityType,
            rawValue,
            displayValue,
            normalizedValue,
            confidence,
            warnings);
    }

    private static bool TryCreateEmptyResult(
        string identityType,
        string? rawValue,
        string displayValue,
        List<IdentityNormalizeWarning> warnings,
        out IdentityNormalizeResult result)
    {
        if (!string.IsNullOrWhiteSpace(displayValue))
        {
            result = null!;
            return false;
        }

        warnings.Add(CreateWarning(
            IdentityNormalizeWarningCodes.EmptyValue,
            "The identity value is empty."));
        warnings.Add(CreateWarning(
            IdentityNormalizeWarningCodes.NormalizedValueEmpty,
            "Normalization did not produce a stable value."));

        result = CreateResult(
            identityType,
            rawValue,
            string.Empty,
            string.Empty,
            IdentityNormalizationConfidence.Unknown,
            warnings);

        return true;
    }

    private static IdentityNormalizeResult CreateResult(
        string identityType,
        string? rawValue,
        string displayValue,
        string normalizedValue,
        string confidence,
        IEnumerable<IdentityNormalizeWarning> warnings)
    {
        return new IdentityNormalizeResult
        {
            IdentityType = identityType,
            RawValue = rawValue,
            DisplayValue = displayValue,
            NormalizedValue = normalizedValue,
            Confidence = confidence,
            Warnings = warnings.ToArray()
        };
    }

    private static IdentityNormalizeWarning CreateWarning(string code, string message)
    {
        return new IdentityNormalizeWarning
        {
            Code = code,
            Message = message
        };
    }

    private static string CanonicalizeIdentityType(string identityType)
    {
        var builder = new StringBuilder(identityType.Trim().Length);
        foreach (var character in identityType.Trim())
        {
            builder.Append(character is ' ' or '-' ? '_' : char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static string ExtractDigits(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string? CompactInternationalPhone(string value)
    {
        var digits = ExtractDigits(value);
        if (digits.Length == 0)
        {
            return null;
        }

        return string.Concat("+", digits);
    }

    private static string FormatUsPhoneNumber(string nationalNumber)
    {
        return string.Create(12, nationalNumber, static (span, source) =>
        {
            source[..3].CopyTo(span);
            span[3] = '-';
            source.AsSpan(3, 3).CopyTo(span[4..]);
            span[7] = '-';
            source.AsSpan(6, 4).CopyTo(span[8..]);
        });
    }

    private static bool TryExtractHandleFromUrl(string value, out string? handle, out bool ambiguous)
    {
        ambiguous = false;
        handle = null;

        var uriCandidate = value.Contains("://", StringComparison.Ordinal)
            ? value
            : value.Contains(".com/", StringComparison.OrdinalIgnoreCase)
              || value.Contains(".net/", StringComparison.OrdinalIgnoreCase)
              || value.Contains(".org/", StringComparison.OrdinalIgnoreCase)
                ? $"https://{value}"
                : null;

        if (uriCandidate is null || !Uri.TryCreate(uriCandidate, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
        {
            ambiguous = true;
            return false;
        }

        string? candidate = host switch
        {
            "instagram.com" or "www.instagram.com" => segments.Length == 1
                ? segments[0]
                : null,
            "twitter.com" or "www.twitter.com" or "x.com" or "www.x.com" => IsReservedSocialPath(segments[0])
                ? null
                : segments[0],
            "tiktok.com" or "www.tiktok.com" => segments[0].StartsWith('@')
                ? segments[0]
                : null,
            "snapchat.com" or "www.snapchat.com" => segments.Length >= 2 && segments[0].Equals("add", StringComparison.OrdinalIgnoreCase)
                ? segments[1]
                : null,
            _ => segments.Length == 1
                ? segments[0]
                : null
        };

        if (candidate is null)
        {
            ambiguous = true;
            return false;
        }

        candidate = candidate.Trim().TrimStart('@');
        if (!IsValidDirectHandle(candidate))
        {
            ambiguous = true;
            return false;
        }

        handle = candidate;
        return true;
    }

    private static bool TryExtractHandleFromPrefix(string value, out string? handle)
    {
        handle = null;
        var separatorIndex = value.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        if (value.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = value[(separatorIndex + 1)..].Trim().TrimStart('@');
        if (!IsValidDirectHandle(candidate))
        {
            return false;
        }

        handle = candidate;
        return true;
    }

    private static bool IsValidDirectHandle(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(' '))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '.' or '-')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsValidBasicEmail(string value)
    {
        return EmailShapeRegex().IsMatch(value);
    }

    private static bool IsReservedSocialPath(string value)
    {
        return value.Equals("home", StringComparison.OrdinalIgnoreCase)
               || value.Equals("explore", StringComparison.OrdinalIgnoreCase)
               || value.Equals("search", StringComparison.OrdinalIgnoreCase)
               || value.Equals("settings", StringComparison.OrdinalIgnoreCase)
               || value.Equals("messages", StringComparison.OrdinalIgnoreCase)
               || value.Equals("hashtag", StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string value)
    {
        return WhitespaceRegex().Replace(value.Trim(), " ");
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailShapeRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
