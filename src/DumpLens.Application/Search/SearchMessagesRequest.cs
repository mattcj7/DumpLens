namespace DumpLens.Application.Search;

public sealed record SearchMessagesRequest
{
    public required string CaseId { get; init; }

    public required string CaseDatabasePath { get; init; }

    public required string QueryText { get; init; }

    public int? MaxResults { get; init; }

    public string? CorrelationId { get; init; }
}
