using System.Windows.Input;

namespace DumpLens.App.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Func<bool>? _canExecute;
    private readonly Action _execute;
    private readonly SynchronizationContext? _synchronizationContext;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _synchronizationContext = SynchronizationContext.Current;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    public void Execute(object? parameter)
    {
        _execute();
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChangedDispatcher.Raise(this, CanExecuteChanged, _synchronizationContext);
    }
}
