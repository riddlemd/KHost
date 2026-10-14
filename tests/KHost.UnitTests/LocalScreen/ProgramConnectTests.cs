using KHost.IPC.SignalR;
using KHost.IPC.SignalR.Contracts;
using KHost.LocalScreen;
using KHost.UnitTests.IPC.SignalR;
using Microsoft.Extensions.Logging;

namespace KHost.UnitTests.LocalScreen;

/// <summary>The screen is often launched before the host is listening; a first connect that gives up
/// leaves it on "Lost the host" until someone relaunches it.</summary>
public sealed class ProgramConnectTests : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly LoopbackScreenHost _host = new();
    private readonly RecordingLog _log = new();
    private readonly CancellationTokenSource _closing = new();

    [Fact]
    public async Task ConnectUntilUpAsync_HostStartsLate_BacksOffThenConnectsOnceItAnswers()
    {
        var key = _host.Provision("Screen 1");
        await using var client = new ScreenClient(null);
        var steps = new List<string>();

        await Program.ConnectUntilUpAsync(
            async cancellationToken =>
            {
                steps.Add("connect");
                await client.ConnectAsync(_host.Url, "Screen 1", authKey: key, cancellationToken: cancellationToken);
            },
            () => client.LastRefusalReason,
            _log,
            _host.Url,
            async (wait, _) =>
            {
                steps.Add($"wait {wait.TotalSeconds}");
                if (steps.Count(s => s.StartsWith("wait")) == 6) await _host.StartAsync();
            },
            _closing.Token).WaitAsync(Patience);

        Assert.Equal(
            ["connect", "wait 1", "connect", "wait 2", "connect", "wait 4", "connect", "wait 8",
                "connect", "wait 15", "connect", "wait 15", "connect"],
            steps);
        Assert.Equal(ScreenClientState.Connected, client.State);
        Assert.Equal("Screen 1", await _host.RegisteredScreenIdAsync());
    }

    [Fact]
    public async Task ConnectUntilUpAsync_RefusedWhileAnotherScreenHoldsTheSlot_ConnectsOnceItFrees()
    {
        await _host.StartAsync();
        var holderKey = _host.Provision("Screen 1");
        var key = _host.Provision("Screen 2");
        await using var holder = new ScreenClient(null);
        await holder.ConnectAsync(_host.Url, "Screen 1", authKey: holderKey);
        await using var client = new ScreenClient(null);
        var waits = 0;

        await Program.ConnectUntilUpAsync(
            cancellationToken => client.ConnectAsync(_host.Url, "Screen 2", authKey: key, cancellationToken: cancellationToken),
            () => client.LastRefusalReason,
            _log,
            _host.Url,
            async (_, cancellationToken) =>
            {
                if (++waits != 2) return;

                await holder.DisconnectAsync();
                while (await _host.RegisteredScreenIdAsync() is not null) await Task.Delay(20, cancellationToken);
            },
            _closing.Token).WaitAsync(Patience);

        Assert.Equal(ScreenClientState.Connected, client.State);
        Assert.Equal("Screen 2", await _host.RegisteredScreenIdAsync());

        var refused = Assert.Single(_log.Containing("Registration refused by the host"));
        Assert.Equal(LogLevel.Warning, refused.Level);
        Assert.Contains("already registered", refused.Message);
        var stillRefused = Assert.Single(_log.Containing("Still refused by the host"));
        Assert.Equal(LogLevel.Debug, stillRefused.Level);
        Assert.True(_log.Entries.ToList().IndexOf(refused) < _log.Entries.ToList().IndexOf(stillRefused), "the first refusal was not the one warned about");
        Assert.Empty(_log.Containing("Could not reach"));
    }

    [Fact]
    public async Task ConnectUntilUpAsync_HostNeverAnswers_WarnsOnceThenLogsEachLaterFailureQuietly()
    {
        var waits = 0;

        await Program.ConnectUntilUpAsync(
            _ => throw new HttpRequestException("Connection refused"),
            () => null,
            _log,
            "http://127.0.0.1:1/ipc/screen",
            (_, _) =>
            {
                if (++waits == 3) _closing.Cancel();
                return Task.CompletedTask;
            },
            _closing.Token).WaitAsync(Patience);

        var failures = _log.Entries.Where(e => e.Message.Contains("ould not reach", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, failures.Count);
        Assert.Equal(LogLevel.Warning, failures[0].Level);
        Assert.IsType<HttpRequestException>(failures[0].Exception);
        Assert.All(failures.Skip(1), e => Assert.Equal(LogLevel.Debug, e.Level));
    }

    [Fact]
    public async Task ConnectUntilUpAsync_WindowClosesDuringABackoffWait_ReturnsPromptly()
    {
        var attempts = 0;
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var loop = Program.ConnectUntilUpAsync(
            _ =>
            {
                attempts++;
                throw new HttpRequestException("Connection refused");
            },
            () => null,
            _log,
            "http://127.0.0.1:1/ipc/screen",
            async (_, cancellationToken) =>
            {
                waiting.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            _closing.Token);

        await waiting.Task.WaitAsync(Patience);
        _closing.Cancel();

        await loop.WaitAsync(Patience);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ConnectUntilUpAsync_WindowClosesMidAttempt_ReturnsWithoutLoggingAFailure()
    {
        var attempting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waits = 0;

        var loop = Program.ConnectUntilUpAsync(
            async cancellationToken =>
            {
                attempting.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            () => null,
            _log,
            "http://127.0.0.1:1/ipc/screen",
            (_, _) =>
            {
                waits++;
                return Task.CompletedTask;
            },
            _closing.Token);

        await attempting.Task.WaitAsync(Patience);
        _closing.Cancel();

        await loop.WaitAsync(Patience);
        Assert.Equal(0, waits);
        Assert.DoesNotContain(_log.Entries, e => e.Level >= LogLevel.Warning);
    }

    public async ValueTask DisposeAsync()
    {
        _closing.Dispose();
        await _host.DisposeAsync();
    }
}
