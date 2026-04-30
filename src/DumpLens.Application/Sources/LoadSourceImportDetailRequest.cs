namespace DumpLens.Application.Sources;

public sealed record LoadSourceImportDetailRequest
{
    public required string CaseId { get; init; }

    public required string CaseDatabasePath { get; init; }

    public required string CasePackageRootPath { get; init; }

    public required string SourceImportId { get; init; }
}
