using System.Data;
using DumpLens.Application.Imports;

namespace DumpLens.App.ViewModels;

public sealed class ImportPreviewGridViewModel : ObservableObject
{
    private int _columnCount;
    private int _rowCount;
    private DataView _rowsView;

    public ImportPreviewGridViewModel()
    {
        _rowsView = CreateEmptyView();
    }

    public int ColumnCount
    {
        get => _columnCount;
        private set => SetProperty(ref _columnCount, value);
    }

    public bool HasRows => RowCount > 0;

    public int RowCount
    {
        get => _rowCount;
        private set => SetProperty(ref _rowCount, value);
    }

    public DataView RowsView
    {
        get => _rowsView;
        private set => SetProperty(ref _rowsView, value);
    }

    public string SummaryText =>
        RowCount == 0
            ? "No preview rows are loaded yet."
            : $"{RowCount} preview row(s) across {ColumnCount} data column(s).";

    public void Clear()
    {
        RowsView = CreateEmptyView();
        ColumnCount = 0;
        RowCount = 0;
        NotifyShapeChanged();
    }

    public void Load(
        IReadOnlyList<ImportPreviewColumn> columns,
        IReadOnlyList<ImportPreviewRow> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        var table = new DataTable("ImportPreview");
        table.Columns.Add("Row", typeof(int));

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Row"
        };

        foreach (var column in columns)
        {
            var displayName = BuildDisplayColumnName(column, usedNames);
            table.Columns.Add(displayName, typeof(string));
        }

        foreach (var row in rows)
        {
            var values = new object[columns.Count + 1];
            values[0] = row.RowNumber;

            for (var index = 0; index < columns.Count; index++)
            {
                values[index + 1] = row.Values.Count > index && row.Values[index] is not null
                    ? row.Values[index]!
                    : string.Empty;
            }

            table.Rows.Add(values);
        }

        RowsView = table.DefaultView;
        ColumnCount = columns.Count;
        RowCount = rows.Count;
        NotifyShapeChanged();
    }

    private static string BuildDisplayColumnName(ImportPreviewColumn column, ISet<string> usedNames)
    {
        var baseName = string.IsNullOrWhiteSpace(column.SourceColumnName)
            ? $"Column {column.Ordinal + 1}"
            : column.SourceColumnName.Trim();
        var candidate = baseName;
        var suffix = 2;

        while (!usedNames.Add(candidate))
        {
            candidate = $"{baseName} ({suffix})";
            suffix++;
        }

        return candidate;
    }

    private static DataView CreateEmptyView()
    {
        var table = new DataTable("ImportPreview");
        table.Columns.Add("Row", typeof(int));
        return table.DefaultView;
    }

    private void NotifyShapeChanged()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(SummaryText));
    }
}
