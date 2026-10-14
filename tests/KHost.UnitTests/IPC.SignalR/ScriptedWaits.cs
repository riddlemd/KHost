namespace KHost.UnitTests.IPC.SignalR;

/// <summary>A clock whose every timer fires when the task its due time is handed to completes, so a
/// test decides when each backoff step ends and sees how long it asked for.</summary>
/// <remarks>A cancelled <c>Task.Delay</c> disposes its timer, so a wait that never completes still
/// ends on cancellation.</remarks>
internal sealed class ScriptedWaits(Func<TimeSpan, Task> wait) : TimeProvider
{
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        new ScriptedTimer(wait(dueTime), callback, state);

    private sealed class ScriptedTimer : ITimer
    {
        private volatile bool _disposed;

        public ScriptedTimer(Task due, TimerCallback callback, object? state) =>
            due.ContinueWith(_ => { if (!_disposed) callback(state); }, TaskScheduler.Default);

        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;

        public void Dispose() => _disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
