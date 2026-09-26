using KHost.IPC.SignalR.Contracts;
using KHost.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.LocalScreen;

/// <summary>What the screen tells the host about a song's end and a lead-in hold: each said
/// outright, and at once, rather than left for the host to infer from a position.</summary>
public class ScreenIpcControllerSongEndTests : IAsyncDisposable
{
    private readonly IScreenClient _client = Substitute.For<IScreenClient>();
    private readonly StreamMediaPlayer _player = new(NullLogger<StreamMediaPlayer>.Instance);
    private readonly List<IScreenState> _sentToHost = [];
    private readonly ScreenIpcController _controller;

    public ScreenIpcControllerSongEndTests()
    {
        _client.SendStateAsync(Arg.Do<IScreenState>(_sentToHost.Add)).Returns(Task.CompletedTask);
        _controller = new ScreenIpcController(_client, _player, NullLogger<ScreenIpcController>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _controller.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EndedFromThePage_ReportsTheEndToTheHostOnce()
    {
        _player.HandleBrowserMessage("""{"type":"ended","position":180,"sampledAtEpochMs":1790000000000}""");

        var state = Assert.IsType<ScreenPlaybackState>(Assert.Single(_sentToHost));
        Assert.True(state.HasEnded);
        Assert.False(state.IsPlaying);
    }

    /// <summary>Stamped at the end itself: the host drops an end sampled before its last seek.</summary>
    [Fact]
    public void EndedFromThePage_CarriesThePositionAndTimeItEndedAt()
    {
        _player.LoadStream("http://host/media/a/stream.m3u8", TimeSpan.FromSeconds(30));

        _player.HandleBrowserMessage("""{"type":"ended","position":150,"sampledAtEpochMs":1790000000000}""");

        var state = Assert.IsType<ScreenPlaybackState>(Assert.Single(_sentToHost));
        Assert.Equal(TimeSpan.FromSeconds(180), state.Position);
        Assert.Equal(DateTime.UnixEpoch.AddMilliseconds(1790000000000), state.SampledAtUtc);
    }

    [Fact]
    public async Task APeriodicReport_NeverSaysTheSongEnded()
    {
        _player.HandleBrowserMessage("""{"type":"state","position":10,"playing":true,"sampledAtEpochMs":1790000000000}""");

        await _controller.SendCurrentStateAsync();

        var state = Assert.IsType<ScreenPlaybackState>(Assert.Single(_sentToHost));
        Assert.False(state.HasEnded);
    }

    /// <summary>Sent the moment the hold starts, not a periodic report later, or the host's clock
    /// runs on into the hold meanwhile.</summary>
    [Fact]
    public void AHoldStarting_IsReportedAtOnce()
    {
        _player.HandleBrowserMessage("""{"type":"state","position":0,"playing":true,"holding":true,"sampledAtEpochMs":1790000000000}""");

        var state = Assert.IsType<ScreenPlaybackState>(Assert.Single(_sentToHost));
        Assert.True(state.IsHolding);
    }

    [Fact]
    public void AHoldRunningOut_IsReportedAtOnce()
    {
        _player.HandleBrowserMessage("""{"type":"state","position":0,"playing":true,"holding":true,"sampledAtEpochMs":1790000000000}""");
        _player.HandleBrowserMessage("""{"type":"state","position":0.1,"playing":true,"holding":false,"sampledAtEpochMs":1790000000100}""");

        Assert.Equal(2, _sentToHost.Count);
        Assert.False(Assert.IsType<ScreenPlaybackState>(_sentToHost[1]).IsHolding);
    }

    [Fact]
    public void AReportThatLeavesTheHoldAsItWas_SendsNothingExtra()
    {
        _player.HandleBrowserMessage("""{"type":"state","position":10,"playing":true,"sampledAtEpochMs":1790000000000}""");

        Assert.Empty(_sentToHost);
    }
}
