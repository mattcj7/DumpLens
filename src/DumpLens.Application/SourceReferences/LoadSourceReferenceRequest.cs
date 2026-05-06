namespace DumpLens.Application.SourceReferences;

public sealed record LoadSourceReferenceRequest
{
    public required string CaseId { get; init; }

    public required string CaseDatabasePath { get; init; }

    public required string CasePackageRootPath { get; init; }

    public required string SourceImportId { get; init; }

    public string? SourceArtifactId { get; init; }

    public string? MessageId { get; init; }

    public string? CorrelationId { get; init; }
}
