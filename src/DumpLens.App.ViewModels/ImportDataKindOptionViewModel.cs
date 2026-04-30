namespace DumpLens.App.ViewModels;

public sealed class ImportDataKindOptionViewModel
{
    public ImportDataKindOptionViewModel(
        ImportDataKind dataKind,
        string label,
        string description)
    {
        DataKind = dataKind;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    public ImportDataKind DataKind { get; }

    public string Description { get; }

    public string Label { get; }
}
