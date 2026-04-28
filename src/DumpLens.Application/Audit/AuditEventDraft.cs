namespace DumpLens.Application.Audit;

public sealed class AuditEventDraft
{
    public string? Id { get; init; }

    public string? CaseId { get; init; }

    public string? UserId { get; init; }

    public string ActionType { get; init; } = string.Empty;

    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string? OldValueJson { get; init; }

    public string? NewValueJson { get; init; }

    public string? Reason { get; init; }

    public DateTimeOffset? EventTimeUtc { get; init; }

    public string? Workstation { get; init; }

    public string? AppVersion { get; init; }

    public string? CorrelationId { get; init; }
}
