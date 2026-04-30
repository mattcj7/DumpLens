using System.Globalization;

namespace DumpLens.Application.Conversations;

public static class ConversationGroupingRules
{
    public const string ParticipantGroupKind = "participant_set";
    public const string ThreadGroupKind = "source_thread";

    public static string? NormalizePlatform(string? platform)
    {
        return string.IsNullOrWhiteSpace(platform)
            ? null
            : platform.Trim().ToLowerInvariant();
    }

    public static string? NormalizeParticipantKey(IEnumerable<string?> participantIdentityIds)
    {
        ArgumentNullException.ThrowIfNull(participantIdentityIds);

        var normalizedIds = participantIdentityIds
            .Where(static identityId => !string.IsNullOrWhiteSpace(identityId))
            .Select(static identityId => identityId!.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static identityId => identityId, StringComparer.Ordinal)
            .ToArray();

        return normalizedIds.Length == 0
            ? null
            : string.Join("|", normalizedIds);
    }

    public static string BuildParticipantGroupKey(string? platform, string normalizedParticipantKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedParticipantKey);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{ParticipantGroupKind}|{NormalizePlatform(platform) ?? string.Empty}|{normalizedParticipantKey.Trim()}");
    }

    public static string BuildThreadGroupKey(string? platform, string sourceThreadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceThreadId);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{ThreadGroupKind}|{NormalizePlatform(platform) ?? string.Empty}|{sourceThreadId.Trim()}");
    }

    public static string BuildSafeTitle(string? platform, int participantCount)
    {
        var platformLabel = NormalizePlatform(platform);
        var baseTitle = platformLabel is null
            ? "Conversation"
            : string.Create(CultureInfo.InvariantCulture, $"{platformLabel} conversation");

        if (participantCount <= 0)
        {
            return baseTitle;
        }

        var participantLabel = participantCount == 1 ? "participant" : "participants";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{baseTitle} ({participantCount.ToString(CultureInfo.InvariantCulture)} {participantLabel})");
    }
}
