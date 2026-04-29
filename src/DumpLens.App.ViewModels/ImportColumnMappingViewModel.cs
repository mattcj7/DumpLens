using System.Collections.ObjectModel;
using DumpLens.Application.Imports;

namespace DumpLens.App.ViewModels;

public sealed class ImportColumnMappingViewModel : ObservableObject
{
    public const string UnmappedOption = "(Not mapped)";

    private string _selectedSourceColumnName;

    public ImportColumnMappingViewModel(
        string dumpLensFieldName,
        string displayName,
        IReadOnlyList<string> availableSourceColumns,
        ImportFieldMappingSuggestion? suggestion)
    {
        DumpLensFieldName = dumpLensFieldName ?? throw new ArgumentNullException(nameof(dumpLensFieldName));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        SuggestedSourceColumnName = suggestion?.SourceColumnName;
        IsAmbiguous = suggestion?.IsAmbiguous ?? false;
        CandidateSummary = BuildCandidateSummary(suggestion);
        AvailableSourceColumns = new ObservableCollection<string>(BuildAvailableOptions(availableSourceColumns));
        _selectedSourceColumnName = string.IsNullOrWhiteSpace(SuggestedSourceColumnName)
            ? UnmappedOption
            : SuggestedSourceColumnName;
    }

    public ObservableCollection<string> AvailableSourceColumns { get; }

    public string CandidateSummary { get; }

    public string DisplayName { get; }

    public string DumpLensFieldName { get; }

    public bool IsAmbiguous { get; }

    public string SelectedSourceColumnName
    {
        get => _selectedSourceColumnName;
        set => SetProperty(ref _selectedSourceColumnName, value);
    }

    public string? SelectedSourceColumnNameOrNull =>
        string.Equals(SelectedSourceColumnName, UnmappedOption, StringComparison.Ordinal)
            ? null
            : SelectedSourceColumnName;

    public string? SuggestedSourceColumnName { get; }

    private static IReadOnlyList<string> BuildAvailableOptions(IReadOnlyList<string> availableSourceColumns)
    {
        var options = new List<string>(availableSourceColumns.Count + 1)
        {
            UnmappedOption
        };

        foreach (var sourceColumn in availableSourceColumns)
        {
            if (!string.IsNullOrWhiteSpace(sourceColumn))
            {
                options.Add(sourceColumn);
            }
        }

        return options;
    }

    private static string BuildCandidateSummary(ImportFieldMappingSuggestion? suggestion)
    {
        if (suggestion is null)
        {
            return "No suggested source column was detected.";
        }

        if (!suggestion.IsAmbiguous)
        {
            return string.IsNullOrWhiteSpace(suggestion.SourceColumnName)
                ? "No suggested source column was detected."
                : $"Suggested source column: {suggestion.SourceColumnName}.";
        }

        if (suggestion.CandidateSourceColumnNames.Count == 0)
        {
            return "Multiple source columns may fit this field. Review the mapping.";
        }

        return $"Multiple possible source columns: {string.Join(", ", suggestion.CandidateSourceColumnNames)}.";
    }
}
