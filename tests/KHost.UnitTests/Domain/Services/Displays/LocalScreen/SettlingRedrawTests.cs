using System.Collections.Concurrent;
using KHost.Domain.Services.Displays.LocalScreen;
using Microsoft.Extensions.Time.Testing;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

public class SettlingRedrawTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new();
    private readonly ConcurrentQueue<Part> _drawn = new();
    private readonly ConcurrentQueue<Exception> _failures = new();

    [Flags]
    public enum Part
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4,
    }

    [Fact]
    public async Task ABurst_WithinTheSettle_IsDrawnOnce_WithEveryPartOwed()
    {
        var redraw = Build(Record);

        redraw.Request(Part.A);
        redraw.Request(Part.B);
        redraw.Request(Part.C);

        await AdvanceUntilAsync(() => _drawn.Count >= 1);
        await SettleFurtherAsync();

        Assert.Equal([Part.A | Part.B | Part.C], _drawn);
    }

    [Fact]
    public async Task Request_IsNotDrawn_BeforeTheSettleElapses()
    {
        var redraw = Build(Record);

        redraw.Request(Part.A);

        // Real time on purpose: nothing but the fake clock may release the draw.
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.Empty(_drawn);

        await AdvanceUntilAsync(() => _drawn.Count >= 1);
        Assert.Equal([Part.A], _drawn);
    }

    [Fact]
    public async Task ARequestDuringADraw_IsDrawnOnceMore_AfterIt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var redraw = Build(async part =>
        {
            _drawn.Enqueue(part);
            if (_drawn.Count == 1)
            {
                entered.SetResult();
                await release.Task;
            }
        });

        redraw.Request(Part.A);
        await AdvanceUntilAsync(() => entered.Task.IsCompleted);

        redraw.Request(Part.B);
        redraw.Request(Part.C);
        release.SetResult();

        await AdvanceUntilAsync(() => _drawn.Count >= 2);
        await SettleFurtherAsync();

        Assert.Equal([Part.A, Part.B | Part.C], _drawn);
    }

    [Fact]
    public async Task ARequestAfterTheLastDrawFinished_IsDrawnAgain()
    {
        var redraw = Build(Record);

        redraw.Request(Part.A);
        await AdvanceUntilAsync(() => _drawn.Count >= 1);
        await SettleFurtherAsync();

        redraw.Request(Part.B);
        await AdvanceUntilAsync(() => _drawn.Count >= 2);

        Assert.Equal([Part.A, Part.B], _drawn);
    }

    [Fact]
    public async Task ADrawThatThrows_IsReported_AndTheNextRequestStillDraws()
    {
        var redraw = Build(part =>
        {
            _drawn.Enqueue(part);
            return part == Part.A ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
        });

        redraw.Request(Part.A);
        await AdvanceUntilAsync(() => !_failures.IsEmpty);
        await SettleFurtherAsync();

        redraw.Request(Part.B);
        await AdvanceUntilAsync(() => _drawn.Count >= 2);

        Assert.Equal("boom", Assert.Single(_failures).Message);
        Assert.Equal([Part.A, Part.B], _drawn);
    }

    private SettlingRedraw<Part> Build(Func<Part, Task> draw)
        => new(Settle, draw, _failures.Enqueue, _time);

    private Task Record(Part part)
    {
        _drawn.Enqueue(part);
        return Task.CompletedTask;
    }

    // The drain registers its delay on the thread pool, so the clock is stepped until it is seen.
    private async Task AdvanceUntilAsync(Func<bool> done)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!done())
        {
            Assert.True(DateTime.UtcNow < deadline, "The redraw never happened.");
            _time.Advance(Settle);
            await Task.Delay(1);
        }
    }

    // Gives a stray second drain, or a drain that never stops, the time to show itself.
    private async Task SettleFurtherAsync()
    {
        for (var i = 0; i < 5; i++)
        {
            _time.Advance(Settle);
            await Task.Delay(10);
        }
    }
}
