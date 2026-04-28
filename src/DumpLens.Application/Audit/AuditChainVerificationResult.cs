namespace DumpLens.Application.Audit;

public sealed class AuditChainVerificationResult
{
    public string? CaseId { get; init; }

    public bool IsValid { get; init; }

    public int CheckedEventCount { get; init; }

    public string? FirstInvalidAuditEventId { get; init; }

    public string FailureCode { get; init; } = AuditChainFailureCodes.None;

    public string? Reason { get; init; }
}
