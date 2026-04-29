namespace DumpLens.Application.Sources;

public interface ISourceImportRepository
{
    Task<bool> CaseExistsAsync(
        string connectionString,
        string caseId,
        CancellationToken cancellationToken = default);

    Task InsertAsync(
        string connectionString,
        SourceImportRecord record,
        CancellationToken cancellationToken = default);
}
