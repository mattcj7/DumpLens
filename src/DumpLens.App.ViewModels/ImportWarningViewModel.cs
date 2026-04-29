using DumpLens.Application.Imports;

namespace DumpLens.App.ViewModels;

public sealed class ImportWarningViewModel
{
    public ImportWarningViewModel(ImportWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        Code = warning.Code;
        Message = warning.Message;
        WorksheetName = warning.WorksheetName;
        RowNumber = warning.RowNumber;
        ColumnName = warning.ColumnName;
        Title = HumanizeWarningCode(warning.Code);
        LocationSummary = BuildLocationSummary(warning);
        DetailText = BuildDetailText(warning);
    }

    public string Code { get; }

    public string? ColumnName { get; }

    public string DetailText { get; }

    public string LocationSummary { get; }

    public string Message { get; }

    public int? RowNumber { get; }

    public string Title { get; }

    public string? WorksheetName { get; }

    private static string BuildDetailText(ImportWarning warning)
    {
        if (string.IsNullOrWhiteSpace(BuildLocationSummary(warning)))
        {
            return warning.Message;
        }

        return $"{warning.Message}{Environment.NewLine}{Environment.NewLine}{BuildLocationSummary(warning)}";
    }

    private static string BuildLocationSummary(ImportWarning warning)
    {
        var parts = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(warning.WorksheetName))
        {
            parts.Add($"Worksheet: {warning.WorksheetName}");
        }

        if (warning.RowNumber.HasValue)
        {
            parts.Add($"Row: {warning.RowNumber.Value}");
        }

        if (!string.IsNullOrWhiteSpace(warning.ColumnName))
        {
            parts.Add($"Column: {warning.ColumnName}");
        }

        return parts.Count == 0
            ? string.Empty
            : string.Join(" | ", parts);
    }

    private static string HumanizeWarningCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "Warning";
        }

        var segments = code
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static segment => char.ToUpperInvariant(segment[0]) + segment[1..])
            .ToArray();

        return segments.Length == 0
            ? "Warning"
            : string.Join(' ', segments);
    }
}
