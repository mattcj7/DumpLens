using System.Windows.Input;

namespace DumpLens.App.ViewModels;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<bool>? _canExecute;
    private readonly Func<Task> _executeAsync;
    private readonly SynchronizationContext? _synchronizationContext;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
        _synchronizationContext = SynchronizationContext.Current;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _executeAsync().ConfigureAwait(false);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChangedDispatcher.Raise(this, CanExecuteChanged, _synchronizationContext);
    }
}
