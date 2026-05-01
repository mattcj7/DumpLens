namespace DumpLens.Application.Search;

public sealed record RebuildMessageSearchIndexRequest
{
    public required string CaseId { get; init; }

    public required string CaseDatabasePath { get; init; }

    public string? CorrelationId { get; init; }
}
