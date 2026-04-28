namespace DumpLens.App.ViewModels;

public sealed class PlaceholderWorkspaceViewModel
{
    public PlaceholderWorkspaceViewModel(
        string title,
        string description,
        string bodyText,
        string nextStepText)
    {
        Title = title;
        Description = description;
        BodyText = bodyText;
        NextStepText = nextStepText;
    }

    public string Title { get; }

    public string Description { get; }

    public string BodyText { get; }

    public string NextStepText { get; }
}
