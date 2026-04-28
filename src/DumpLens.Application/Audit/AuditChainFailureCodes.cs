namespace DumpLens.Application.Audit;

public static class AuditChainFailureCodes
{
    public const string None = "none";
    public const string PreviousHashMismatch = "previous_hash_mismatch";
    public const string CurrentHashMismatch = "current_hash_mismatch";
}
