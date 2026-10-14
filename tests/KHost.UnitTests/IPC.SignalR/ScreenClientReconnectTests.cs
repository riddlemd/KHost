using System.Reflection;
using KHost.IPC.SignalR;
using KHost.IPC.SignalR.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace KHost.UnitTests.IPC.SignalR;

/// <summary>A screen that never wins back its host stays on "Lost the host" for the rest of the night,
/// so the reconnect loop is driven against a host that really goes away and really comes back.</summary>
public sealed class ScreenClientReconnectTests : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    // Short enough that SignalR's own reconnect gives up within the test.
    private static readonly TimeSpan[] QuickAutomaticReconnect = [TimeSpan.Zero, TimeSpan.FromMilliseconds(50)];

    private readonly LoopbackScreenHost _host = new();
    private readonly RecordingLog _log = new();

    [Fact]
    public async Task HostDownPastSignalRsOwnRetries_IsWonBackAndRegisteredAgain()
    {
        await using var outage = await RideOutAnOutageAsync();

        Assert.Equal(ScreenClientState.Connected, outage.Client.State);
        Assert.Equal("Screen 1", await _host.RegisteredScreenIdAsync());
        // SignalR's own reconnect ran and gave up before the loop took the link over.
        Assert.NotEmpty(_log.Containing("SignalR reconnecting"));
        Assert.NotEmpty(_log.Containing("Reconnected to the host on attempt 6"));
    }

    [Fact]
    public async Task HostDownPastSignalRsOwnRetries_RetriesAfterOneTwoFourEightThenEveryFifteenSeconds()
    {
        await using var outage = await RideOutAnOutageAsync();

        Assert.Equal([1, 2, 4, 8, 15, 15], outage.Waits.Select(w => w.TotalSeconds));
    }

    [Fact]
    public async Task HostDownPastSignalRsOwnRetries_WarnsOnceThenLogsEachLaterFailureQuietly()
    {
        await using var outage = await RideOutAnOutageAsync();

        var failures = _log.Entries.Where(e => e.Message.Contains("ould not reconnect", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, failures.Count);
        Assert.Equal(LogLevel.Warning, failures[0].Level);
        Assert.NotNull(failures[0].Exception);
        Assert.All(failures.Skip(1), e => Assert.Equal(LogLevel.Debug, e.Level));
    }

    [Fact]
    public async Task RefusedWhileReconnecting_NamesTheReasonOnce_AndConnectsWhenTheSlotFrees()
    {
        await _host.StartAsync();
        var key = _host.Provision("Screen 1");
        var otherKey = _host.Provision("Screen 2");
        await using var other = new ScreenClient(null);

        var waits = 0;
        var firstWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slotFree = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var client = new ScreenClient(_log, new ScriptedWaits(async _ =>
            {
                switch (Interlocked.Increment(ref waits))
                {
                    case 1:
                        firstWait.TrySetResult();
                        await otherIn.Task;
                        break;
                    case 3:
                        thirdWait.TrySetResult();
                        await slotFree.Task;
                        break;
                }
            }));
        await client.ConnectAsync(_host.Url, "Screen 1", authKey: key);
        var backUp = WhenBackUp(client);

        // A state report the hub cannot read makes it abort the link, which SignalR does not retry.
        await ConnectionOf(client).SendAsync(nameof(ScreenHub.ReceiveStateAsync), "not an envelope");
        await firstWait.Task.WaitAsync(Patience);
        await UntilNoScreenIsRegisteredAsync();
        await other.ConnectAsync(_host.Url, "Screen 2", authKey: otherKey);
        otherIn.SetResult();

        await thirdWait.Task.WaitAsync(Patience);
        await other.DisconnectAsync();
        await UntilNoScreenIsRegisteredAsync();
        slotFree.SetResult();

        await backUp.WaitAsync(Patience);
        Assert.Equal("Screen 1", await _host.RegisteredScreenIdAsync());

        var refused = Assert.Single(_log.Containing("Registration refused by the host"));
        Assert.Equal(LogLevel.Warning, refused.Level);
        Assert.Contains("already registered", refused.Message);
        var stillRefused = Assert.Single(_log.Containing("Still refused by the host"));
        Assert.Equal(LogLevel.Debug, stillRefused.Level);
        Assert.True(_log.Entries.ToList().IndexOf(refused) < _log.Entries.ToList().IndexOf(stillRefused), "the first refusal was not the one warned about");
        Assert.Empty(_log.Containing("Could not reconnect"));
    }

    [Fact]
    public async Task DisconnectAsync_WhileWaitingToReconnect_EndsTheLoopPromptly()
    {
        await _host.StartAsync();
        var key = _host.Provision("Screen 1");

        var waits = 0;
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new ScreenClient(_log, new ScriptedWaits(_ =>
            {
                Interlocked.Increment(ref waits);
                waiting.TrySetResult();
                return new TaskCompletionSource().Task;
            }))
        {
            AutomaticReconnectDelays = QuickAutomaticReconnect,
        };
        await client.ConnectAsync(_host.Url, "Screen 1", authKey: key);

        await _host.StopAsync();
        await waiting.Task.WaitAsync(Patience);

        await client.DisconnectAsync().WaitAsync(Patience);

        await UntilAsync(() => ReconnectLoopRunning(client) == 0, "the reconnect loop never ended");
        Assert.Equal(1, waits);
        Assert.Equal(ScreenClientState.Disconnected, client.State);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>Takes the host down until the loop has backed off past its last step, then brings it back.</summary>
    private async Task<Outage> RideOutAnOutageAsync()
    {
        await _host.StartAsync();
        var key = _host.Provision("Screen 1");

        var waits = new List<TimeSpan>();
        var lastStepReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hostBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var client = new ScreenClient(_log, new ScriptedWaits(async wait =>
            {
                int count;
                lock (waits)
                {
                    waits.Add(wait);
                    count = waits.Count;
                }

                if (count < 6) return;

                lastStepReached.TrySetResult();
                await hostBack.Task;
            }))
        {
            AutomaticReconnectDelays = QuickAutomaticReconnect,
        };
        await client.ConnectAsync(_host.Url, "Screen 1", authKey: key);
        var backUp = WhenBackUp(client);

        await _host.StopAsync();
        await lastStepReached.Task.WaitAsync(Patience);
        await _host.StartAsync();
        hostBack.SetResult();

        await backUp.WaitAsync(Patience);
        lock (waits) { return new Outage(client, [.. waits]); }
    }

    private async Task UntilNoScreenIsRegisteredAsync()
    {
        for (var start = DateTime.UtcNow; DateTime.UtcNow - start < Patience; await Task.Delay(20))
            if (await _host.RegisteredScreenIdAsync() is null) return;

        Assert.Fail("the host never let the screen go");
    }

    private static async Task UntilAsync(Func<bool> condition, string failure)
    {
        for (var start = DateTime.UtcNow; DateTime.UtcNow - start < Patience; await Task.Delay(20))
            if (condition()) return;

        Assert.Fail(failure);
    }

    private static Task WhenBackUp(ScreenClient client)
    {
        var sawReconnecting = false;
        var backUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateChanged += (_, e) =>
        {
            if (e.NewState == ScreenClientState.Reconnecting) sawReconnecting = true;
            else if (e.NewState == ScreenClientState.Connected && sawReconnecting) backUp.TrySetResult();
        };

        return backUp.Task;
    }

    private static HubConnection ConnectionOf(ScreenClient client) => (HubConnection)typeof(ScreenClient)
        .GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;

    private static int ReconnectLoopRunning(ScreenClient client) => (int)typeof(ScreenClient)
        .GetField("_reconnecting", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;

    private sealed record Outage(ScreenClient Client, List<TimeSpan> Waits) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Client.DisposeAsync();
    }
}
