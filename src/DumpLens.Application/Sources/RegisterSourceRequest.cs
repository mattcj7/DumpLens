namespace DumpLens.Application.Sources;

public sealed record RegisterSourceRequest
{
    public required string CaseId { get; init; }

    public required string CaseDatabasePath { get; init; }

    public required string CasePackageRootPath { get; init; }

    public required string SelectedSourceFilePath { get; init; }

    public string? SourceName { get; init; }

    public required string SourceType { get; init; }

    public string? Platform { get; init; }

    public string? OriginalFilenameOverride { get; init; }

    public string? ImportedByUserId { get; init; }

    public string? Notes { get; init; }

    public string? SourceMetadataJson { get; init; }

    public string? CorrelationId { get; init; }

    public string? OwnerPersonId { get; init; }

    public string? DeviceId { get; init; }

    public string? PlatformAccountId { get; init; }

    public string? ExtractionType { get; init; }

    public string? ProviderReturnType { get; init; }
}
