using DumpLens.Application.Imports;

namespace DumpLens.App.ViewModels;

public sealed class ImportSourceTypeOptionViewModel
{
    public ImportSourceTypeOptionViewModel(
        ImportSourceKind sourceKind,
        string label,
        string description,
        string supportedExtensions,
        bool isAvailable)
    {
        SourceKind = sourceKind;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        SupportedExtensions = supportedExtensions ?? throw new ArgumentNullException(nameof(supportedExtensions));
        IsAvailable = isAvailable;
    }

    public string Description { get; }

    public bool IsAvailable { get; }

    public bool IsUnavailable => !IsAvailable;

    public string Label { get; }

    public ImportSourceKind SourceKind { get; }

    public string SupportedExtensions { get; }
}
