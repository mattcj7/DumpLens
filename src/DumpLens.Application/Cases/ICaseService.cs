namespace DumpLens.Application.Cases;

public interface ICaseService
{
    Task<CreateCaseResult> CreateAsync(
        CreateCaseRequest request,
        CancellationToken cancellationToken = default);
}
