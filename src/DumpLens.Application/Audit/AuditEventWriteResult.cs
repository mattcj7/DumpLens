namespace DumpLens.Application.Audit;

public sealed class AuditEventWriteResult
{
    public string CorrelationId { get; init; } = string.Empty;

    public AuditEventRecord AuditEvent { get; init; } = new();
}
