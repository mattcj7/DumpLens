namespace DumpLens.Application.Sources;

public sealed record LoadSourceImportSummariesRequest
{
    public required string CaseId { get; init; }

    public required string CaseDatabasePath { get; init; }

    public required string CasePackageRootPath { get; init; }
}
