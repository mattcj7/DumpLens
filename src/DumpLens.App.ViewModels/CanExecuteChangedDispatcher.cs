namespace DumpLens.App.ViewModels;

internal static class CanExecuteChangedDispatcher
{
    public static void Raise(
        object command,
        EventHandler? handler,
        SynchronizationContext? synchronizationContext)
    {
        if (handler is null)
        {
            return;
        }

        if (synchronizationContext is null || ReferenceEquals(SynchronizationContext.Current, synchronizationContext))
        {
            handler(command, EventArgs.Empty);
            return;
        }

        synchronizationContext.Post(
            static state =>
            {
                var (sender, eventHandler) = ((object Sender, EventHandler Handler))state!;
                eventHandler(sender, EventArgs.Empty);
            },
            (command, handler));
    }
}
