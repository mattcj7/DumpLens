namespace DumpLens.App.ViewModels;

public sealed class ImportWizardStepViewModel : ObservableObject
{
    private bool _isCompleted;
    private bool _isCurrent;

    public ImportWizardStepViewModel(int stepNumber, string title, string description)
    {
        StepNumber = stepNumber;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    public string Description { get; }

    public bool IsCompleted
    {
        get => _isCompleted;
        internal set => SetProperty(ref _isCompleted, value);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        internal set => SetProperty(ref _isCurrent, value);
    }

    public int StepNumber { get; }

    public string Title { get; }
}
