namespace DumpLens.Application.Cases;

public sealed record CaseSummary
{
    public required string CaseId { get; init; }

    public string? CaseNumber { get; init; }

    public required string Title { get; init; }

    public required string CaseStatus { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
