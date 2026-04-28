namespace DumpLens.App.ViewModels;

public sealed class InspectorPlaceholderViewModel
{
    public InspectorPlaceholderViewModel(
        string title,
        string description,
        string bodyText)
    {
        Title = title;
        Description = description;
        BodyText = bodyText;
    }

    public string Title { get; }

    public string Description { get; }

    public string BodyText { get; }
}
