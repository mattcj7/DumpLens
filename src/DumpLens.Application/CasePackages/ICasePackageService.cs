namespace DumpLens.Application.CasePackages;

public interface ICasePackageService
{
    Task<CasePackageCreateResult> CreateAsync(
        CasePackageCreateRequest request,
        CancellationToken cancellationToken = default);
}
