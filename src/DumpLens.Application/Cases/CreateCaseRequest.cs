namespace DumpLens.Application.Cases;

public sealed record CreateCaseRequest
{
    public string? CaseNumber { get; init; }

    public string? Title { get; init; }

    public string? IncidentType { get; init; }

    public DateTimeOffset? IncidentStartUtc { get; init; }

    public DateTimeOffset? IncidentEndUtc { get; init; }

    public string? IncidentTimezone { get; init; }

    public string? IncidentLocationText { get; init; }

    public string? LeadInvestigator { get; init; }

    public string? Agency { get; init; }

    public string? Summary { get; init; }

    public string? ParentDirectoryPath { get; init; }

    public string? RequestedPackageFolderName { get; init; }

    public string? CreatedByUserId { get; init; }

    public string? CreatedByDisplayName { get; init; }

    public string? CorrelationId { get; init; }
}
