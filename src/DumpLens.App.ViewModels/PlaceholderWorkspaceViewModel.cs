namespace DumpLens.App.ViewModels;

public sealed class PlaceholderWorkspaceViewModel : WorkspaceViewModelBase
{
    public PlaceholderWorkspaceViewModel(
        string title,
        string description,
        string bodyText,
        string nextStepText)
        : base(title, description)
    {
        BodyText = bodyText;
        NextStepText = nextStepText;
    }

    public string BodyText { get; }

    public string NextStepText { get; }
}
