using System.Diagnostics;
using KHost.IPC.SignalR;

namespace KHost.UnitTests.IPC.SignalR;

/// <summary>SignalR closes every link once, as the host starts stopping; Kestrel cannot close a WebSocket itself
/// and waits out the whole shutdown timeout on any link opened after that. A screen on the network reconnects the
/// moment it is dropped, so without a guard its reconnect lands in that gap.</summary>
public sealed class ScreenHubShutdownTests : IAsyncDisposable
{
    // The framework's default drain: the exit sat out all of it.
    private static readonly TimeSpan FrameworkShutdownTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly LoopbackScreenHost _host = new() { ShutdownTimeout = FrameworkShutdownTimeout };

    [Fact]
    public async Task Given_AScreenThatReconnectsWhileTheHostStops_When_TheHostStops_Then_ItDoesNotWaitOutTheShutdownTimeout()
    {
        // Given
        await _host.StartAsync();
        var key = _host.Provision("Screen 1");
        await using var client = new ScreenClient(null);
        await client.ConnectAsync(_host.Url, "Screen 1", authKey: key);
        var connectedBefore = _host.ConnectionsSeen;
        // Holds the stop where KHost's own shutdown work runs, until the dropped screen's reconnect has met the hub.
        _host.BeforeServerStops = async _ =>
        {
            var waited = Stopwatch.StartNew();
            while (_host.ConnectionsSeen == connectedBefore && waited.Elapsed < Patience)
                await Task.Delay(20, CancellationToken.None);
        };

        // When
        var clock = Stopwatch.StartNew();
        await _host.StopAsync();
        clock.Stop();

        // Then
        Assert.True(_host.ConnectionsSeen > connectedBefore, "the screen never reconnected while the host stopped");
        Assert.True(clock.Elapsed < Patience, $"the host took {clock.Elapsed} to stop");
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
