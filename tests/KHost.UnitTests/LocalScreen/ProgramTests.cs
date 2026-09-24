using KHost.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.LocalScreen;

public class ProgramTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // A throw that ended the loop would stop position reports for the rest of the night, with
    // nothing on screen to say so.
    [Fact]
    public async Task RepeatAsync_ActionThrows_KeepsRunning()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var reachedThird = new TaskCompletionSource();

        var loop = Program.RepeatAsync(Tick, () =>
        {
            if (Interlocked.Increment(ref calls) >= 3) reachedThird.TrySetResult();
            throw new TimeoutException("send failed");
        }, NullLogger.Instance, cancellation.Token);

        await reachedThird.Task.WaitAsync(Patience);
        cancellation.Cancel();
        await loop.WaitAsync(Patience);

        Assert.True(calls >= 3);
    }

    [Fact]
    public async Task RepeatAsync_Cancelled_CompletesWithoutThrowing()
    {
        using var cancellation = new CancellationTokenSource();
        var ranOnce = new TaskCompletionSource();

        var loop = Program.RepeatAsync(Tick, () =>
        {
            ranOnce.TrySetResult();
            return Task.CompletedTask;
        }, NullLogger.Instance, cancellation.Token);

        await ranOnce.Task.WaitAsync(Patience);
        cancellation.Cancel();
        await loop.WaitAsync(Patience);

        Assert.True(loop.IsCompletedSuccessfully);
    }
}
