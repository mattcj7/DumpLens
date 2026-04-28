namespace DumpLens.App.ViewModels;

public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(
        string label,
        string summary,
        string workspaceDescription,
        string workspaceBodyText,
        string workspaceNextStepText,
        string inspectorDescription,
        string inspectorBodyText)
    {
        Label = label;
        Summary = summary;
        WorkspaceDescription = workspaceDescription;
        WorkspaceBodyText = workspaceBodyText;
        WorkspaceNextStepText = workspaceNextStepText;
        InspectorDescription = inspectorDescription;
        InspectorBodyText = inspectorBodyText;
    }

    public string Label { get; }

    public string Summary { get; }

    public string WorkspaceDescription { get; }

    public string WorkspaceBodyText { get; }

    public string WorkspaceNextStepText { get; }

    public string InspectorDescription { get; }

    public string InspectorBodyText { get; }
}
