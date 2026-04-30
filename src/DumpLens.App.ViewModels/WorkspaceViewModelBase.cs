namespace DumpLens.App.ViewModels;

public abstract class WorkspaceViewModelBase : ObservableObject
{
    private string _description;
    private string _title;

    protected WorkspaceViewModelBase(string title, string description)
    {
        _title = title;
        _description = description;
    }

    public string Description
    {
        get => _description;
        protected set => SetProperty(ref _description, value);
    }

    public string Title
    {
        get => _title;
        protected set => SetProperty(ref _title, value);
    }
}
