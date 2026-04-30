namespace DumpLens.App.ViewModels;

public abstract class InspectorViewModelBase : ObservableObject
{
    private string _description;
    private string _title;

    protected InspectorViewModelBase(string title, string description)
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
