using DumpLens.App.ViewModels;

namespace DumpLens.Tests.Unit.UI;

public sealed class RelayCommandTests
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
            var command = new RelayCommand(() => { });
            var eventThreadIds = new List<int>();
            command.CanExecuteChanged += (_, _) => eventThreadIds.Add(Environment.CurrentManagedThreadId);

            var backgroundThread = new Thread(command.RaiseCanExecuteChanged);
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
    public void RaiseCanExecuteChanged_Without_Synchronization_Context_Raises_Directly()
    {
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            var command = new RelayCommand(() => { });
            var eventRaised = false;
            command.CanExecuteChanged += (_, _) => eventRaised = true;

            command.RaiseCanExecuteChanged();

            Assert.True(eventRaised);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void RaiseCanExecuteChanged_On_Captured_Context_Raises_Without_Posting()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var synchronizationContext = new CommandTestSynchronizationContext(ownerThreadId);
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);

        try
        {
            var command = new RelayCommand(() => { });
            var eventThreadIds = new List<int>();
            command.CanExecuteChanged += (_, _) => eventThreadIds.Add(Environment.CurrentManagedThreadId);

            command.RaiseCanExecuteChanged();

            Assert.Equal([ownerThreadId], eventThreadIds);
            Assert.Equal(0, synchronizationContext.PostCallCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }
}
