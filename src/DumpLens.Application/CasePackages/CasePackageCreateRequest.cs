namespace DumpLens.Application.CasePackages;

public sealed record CasePackageCreateRequest
{
    public required string RootDirectoryPath { get; init; }

    public required string CaseId { get; init; }

    public string? CaseNumber { get; init; }

    public string? Title { get; init; }

    public string? RequestedFolderName { get; init; }

    public CasePackagePreparationMode PreparationMode { get; init; } = CasePackagePreparationMode.Copy;
}
