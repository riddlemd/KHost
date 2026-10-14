using KHost.UserInterface.Components.Panels;

namespace KHost.UnitTests.UserInterface.Components.Panels;

public class CoalescedRefreshTests
{
    [Flags]
    private enum Parts
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4,
    }

    private readonly List<Parts> _runs = [];
    private readonly Queue<TaskCompletionSource> _gates = new();

    private CoalescedRefresh<Parts> Build() => new(parts =>
    {
        _runs.Add(parts);
        return _gates.TryDequeue(out var gate) ? gate.Task : Task.CompletedTask;
    });

    [Fact]
    public async Task RequestAsync_NothingArrivesMeanwhile_RunsOnceWithWhatWasAsked()
    {
        await Build().RequestAsync(Parts.B);

        Assert.Equal([Parts.B], _runs);
    }

    [Fact]
    public async Task RequestAsync_SeveralArriveWhileOneRuns_RunsOneFollowUpCarryingAllOfThem()
    {
        var first = new TaskCompletionSource();
        _gates.Enqueue(first);
        var refresh = Build();

        var running = refresh.RequestAsync(Parts.A);
        var second = refresh.RequestAsync(Parts.B);
        var third = refresh.RequestAsync(Parts.C);

        Assert.Equal([Parts.A], _runs);
        Assert.True(second.IsCompleted);
        Assert.True(third.IsCompleted);

        first.SetResult();
        await running;

        Assert.Equal([Parts.A, Parts.B | Parts.C], _runs);
    }

    [Fact]
    public async Task RequestAsync_AfterABurstHasSettled_RunsAgain()
    {
        var refresh = Build();

        await refresh.RequestAsync(Parts.A);
        await refresh.RequestAsync(Parts.A);

        Assert.Equal([Parts.A, Parts.A], _runs);
    }

    [Fact]
    public async Task RequestAsync_None_RunsNothing()
    {
        await Build().RequestAsync(Parts.None);

        Assert.Empty(_runs);
    }
}
