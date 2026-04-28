namespace DumpLens.Application.Cases;

public interface ICaseRepository
{
    Task<CaseSummary> InsertAsync(
        string connectionString,
        CaseRecord record,
        CancellationToken cancellationToken = default);
}
