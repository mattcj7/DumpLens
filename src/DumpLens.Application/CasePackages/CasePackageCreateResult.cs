namespace DumpLens.Application.CasePackages;

public sealed record CasePackageCreateResult
{
    public required string PackageId { get; init; }

    public required string CaseId { get; init; }

    public required string PackageRootPath { get; init; }

    public required string ManifestPath { get; init; }

    public required string DatabasePath { get; init; }

    public required string DatabaseRelativePath { get; init; }

    public required IReadOnlyDictionary<string, string> Folders { get; init; }

    public required CasePackageManifest Manifest { get; init; }
}
