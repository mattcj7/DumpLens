using System.Security.Cryptography;
using System.Text;

namespace DumpLens.Core.Audit;

public static class AuditChainHash
{
    public const string GenesisMarker = "GENESIS";

    public static string ComputeHash(string? previousHash, string canonicalCurrentEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalCurrentEvent);

        var normalizedPreviousHash = string.IsNullOrWhiteSpace(previousHash)
            ? GenesisMarker
            : previousHash.Trim();
        var payload = $"{normalizedPreviousHash}\n{canonicalCurrentEvent}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
