using System.Collections.ObjectModel;

namespace DumpLens.App.ViewModels;

public sealed class SourceReferenceSectionViewModel
{
    public SourceReferenceSectionViewModel(
        string title,
        IEnumerable<SourceReferenceFieldViewModel>? fields = null,
        string? emptyMessage = null)
    {
        Title = string.IsNullOrWhiteSpace(title)
            ? throw new ArgumentException("A non-empty value is required.", nameof(title))
            : title.Trim();
        Fields = new ObservableCollection<SourceReferenceFieldViewModel>(fields ?? []);
        EmptyMessage = string.IsNullOrWhiteSpace(emptyMessage)
            ? null
            : emptyMessage.Trim();
    }

    public string? EmptyMessage { get; }

    public ObservableCollection<SourceReferenceFieldViewModel> Fields { get; }

    public bool HasFields => Fields.Count > 0;

    public string Title { get; }
}
