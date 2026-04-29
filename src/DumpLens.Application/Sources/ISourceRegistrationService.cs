namespace DumpLens.Application.Sources;

public interface ISourceRegistrationService
{
    Task<RegisterSourceResult> RegisterAsync(
        RegisterSourceRequest request,
        CancellationToken cancellationToken = default);
}
