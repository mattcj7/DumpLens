using DumpLens.App.ViewModels;

namespace DumpLens.Tests.Unit.UI;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public void RaiseCanExecuteChanged_Posts_Back_To_Captured_Synchronization_Context()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var synchronizationContext = new CommandTestSynchronizationContext(ownerThreadId);
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);

        try
        {
            var command = CreateCommand(() => Task.CompletedTask);
            var eventThreadIds = new List<int>();
            SubscribeCanExecuteChanged(command, () => eventThreadIds.Add(Environment.CurrentManagedThreadId));

            var backgroundThread = new Thread(() => InvokeRaiseCanExecuteChanged(command));
            backgroundThread.Start();
            Assert.True(backgroundThread.Join(TimeSpan.FromSeconds(5)), "Background thread did not finish in time.");

            synchronizationContext.DrainPostedCallbacks();

            Assert.Equal([ownerThreadId], eventThreadIds);
            Assert.Equal(1, synchronizationContext.PostCallCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void Execute_Toggles_CanExecute_And_Raises_Notifications_On_Captured_Context()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var synchronizationContext = new CommandTestSynchronizationContext(ownerThreadId);
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);

        try
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var command = CreateCommand(async () => await completion.Task.ConfigureAwait(false));
            var canExecuteStates = new List<bool>();
            var eventThreadIds = new List<int>();
            SubscribeCanExecuteChanged(
                command,
                () =>
                {
                    eventThreadIds.Add(Environment.CurrentManagedThreadId);
                    canExecuteStates.Add(InvokeCanExecute(command));
                });

            InvokeExecute(command);

            Assert.False(InvokeCanExecute(command));
            Assert.Equal([false], canExecuteStates);

            completion.SetResult();
            Assert.True(
                synchronizationContext.WaitForPostedCallback(TimeSpan.FromSeconds(5)),
                "Timed out waiting for AsyncRelayCommand to post CanExecuteChanged back to the captured context.");

            synchronizationContext.DrainPostedCallbacks();

            Assert.True(InvokeCanExecute(command));
            Assert.Equal([false, true], canExecuteStates);
            Assert.All(eventThreadIds, threadId => Assert.Equal(ownerThreadId, threadId));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void RaiseCanExecuteChanged_Without_Synchronization_Context_Raises_Directly()
    {
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            var command = CreateCommand(() => Task.CompletedTask);
            var eventRaised = false;
            SubscribeCanExecuteChanged(command, () => eventRaised = true);

            InvokeRaiseCanExecuteChanged(command);

            Assert.True(eventRaised);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static AsyncRelayCommand CreateCommand(Func<Task> executeAsync)
    {
        return new AsyncRelayCommand(executeAsync);
    }

    private static bool InvokeCanExecute(AsyncRelayCommand command)
    {
        return command.CanExecute(null);
    }

    private static void InvokeExecute(AsyncRelayCommand command)
    {
        command.Execute(null);
    }

    private static void InvokeRaiseCanExecuteChanged(AsyncRelayCommand command)
    {
        command.RaiseCanExecuteChanged();
    }

    private static void SubscribeCanExecuteChanged(AsyncRelayCommand command, Action onRaised)
    {
        command.CanExecuteChanged += (_, _) => onRaised();
    }
}
