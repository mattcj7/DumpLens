namespace DumpLens.Application.Imports;

public sealed record ImportWarning
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? WorksheetName { get; init; }

    public int? RowNumber { get; init; }

    public string? ColumnName { get; init; }
}
