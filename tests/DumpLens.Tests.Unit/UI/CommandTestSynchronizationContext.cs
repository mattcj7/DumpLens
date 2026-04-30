using System.Collections.Concurrent;

namespace DumpLens.Tests.Unit.UI;

internal sealed class CommandTestSynchronizationContext : SynchronizationContext
{
    private readonly int _ownerThreadId;
    private readonly ManualResetEventSlim _postedCallbackEvent = new(initialState: false);
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _postedCallbacks = new();
    private int _postCallCount;

    public CommandTestSynchronizationContext(int ownerThreadId)
    {
        _ownerThreadId = ownerThreadId;
    }

    public int PostCallCount => _postCallCount;

    public override void Post(SendOrPostCallback d, object? state)
    {
        Interlocked.Increment(ref _postCallCount);
        _postedCallbacks.Enqueue((d, state));
        _postedCallbackEvent.Set();
    }

    public void DrainPostedCallbacks()
    {
        Assert.Equal(_ownerThreadId, Environment.CurrentManagedThreadId);

        while (_postedCallbacks.TryDequeue(out var workItem))
        {
            workItem.Callback(workItem.State);
        }

        _postedCallbackEvent.Reset();
    }

    public bool WaitForPostedCallback(TimeSpan timeout)
    {
        return _postedCallbackEvent.Wait(timeout);
    }
}
