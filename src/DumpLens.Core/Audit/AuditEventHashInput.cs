namespace DumpLens.Core.Audit;

public readonly record struct AuditEventHashInput(
    string Id,
    string? CaseId,
    string? UserId,
    string ActionType,
    string? EntityType,
    string? EntityId,
    string Summary,
    string? OldValueJson,
    string? NewValueJson,
    string? Reason,
    DateTimeOffset EventTimeUtc,
    string? Workstation,
    string? AppVersion);
