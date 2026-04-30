namespace DumpLens.App.ViewModels;

public sealed class InspectorPlaceholderViewModel : InspectorViewModelBase
{
    public InspectorPlaceholderViewModel(
        string title,
        string description,
        string bodyText)
        : base(title, description)
    {
        BodyText = bodyText;
    }

    public string BodyText { get; }
}
