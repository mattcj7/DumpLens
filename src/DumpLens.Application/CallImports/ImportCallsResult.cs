namespace DumpLens.Application.CallImports;

public sealed record ImportCallsResult
{
    public required string CaseId { get; init; }

    public required string SourceImportId { get; init; }

    public required int ImportedCallCount { get; init; }

    public required int SourceArtifactCount { get; init; }

    public required int IdentityCountCreated { get; init; }

    public required int IdentityCountReused { get; init; }

    public required int WarningCount { get; init; }

    public string? AuditEventId { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}
