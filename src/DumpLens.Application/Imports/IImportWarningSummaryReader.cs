namespace DumpLens.Application.Imports;

public interface IImportWarningSummaryReader
{
    Task<IReadOnlyList<ImportWarningSummary>> GetSummariesAsync(
        string caseDatabasePath,
        string sourceImportId,
        CancellationToken cancellationToken = default);
}
