namespace DumpLens.Application.Cases;

public sealed record CreateCaseResult
{
    public required string CaseId { get; init; }

    public required string PackageId { get; init; }

    public string? CaseNumber { get; init; }

    public required string Title { get; init; }

    public required string PackageRootPath { get; init; }

    public required string DatabasePath { get; init; }

    public required string ManifestPath { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public string? AuditEventId { get; init; }

    public required string CorrelationId { get; init; }
}
