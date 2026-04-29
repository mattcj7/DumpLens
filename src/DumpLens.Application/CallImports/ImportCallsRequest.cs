using DumpLens.Application.Imports;

namespace DumpLens.Application.CallImports;

public sealed record ImportCallsRequest
{
    public required string CaseId { get; init; }

    public required string SourceImportId { get; init; }

    public required string CaseDatabasePath { get; init; }

    public string? SourceFilePath { get; init; }

    public required ImportSourceKind SourceKind { get; init; }

    public string? WorksheetName { get; init; }

    public required IReadOnlyList<CallImportFieldMapping> FieldMappings { get; init; }

    public string? TimezoneAssumption { get; init; }

    public string? DefaultPlatformOrCarrier { get; init; }

    public string? ImportedByUserId { get; init; }

    public string? CorrelationId { get; init; }

    public int? RowLimit { get; init; }
}
