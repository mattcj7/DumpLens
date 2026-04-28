namespace DumpLens.Application.Cases;

public sealed record CaseRecord
{
    public required string Id { get; init; }

    public string? CaseNumber { get; init; }

    public required string Title { get; init; }

    public string? IncidentType { get; init; }

    public DateTimeOffset? IncidentStartUtc { get; init; }

    public DateTimeOffset? IncidentEndUtc { get; init; }

    public string? IncidentTimezone { get; init; }

    public string? IncidentLocationText { get; init; }

    public string? LeadInvestigator { get; init; }

    public string? Agency { get; init; }

    public string? Summary { get; init; }

    public required string CaseStatus { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
