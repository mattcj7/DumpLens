namespace DumpLens.Application.Audit;

public interface IAuditLogger
{
    Task<AuditEventWriteResult> WriteAsync(
        AuditEventDraft draft,
        CancellationToken cancellationToken = default);

    Task<AuditChainVerificationResult> VerifyChainAsync(
        string? caseId,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
