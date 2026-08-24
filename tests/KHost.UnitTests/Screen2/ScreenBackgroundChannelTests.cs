using KHost.Abstractions.Services.IPC;
using KHost.Screen2;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.Screen2;

/// <summary>
/// The bed rides a second element with no timeline. What matters is that it stays off the song's
/// channel in both directions: a background command must not report the song as having moved, and
/// a bed track ending must not retire the singer's performance.
/// </summary>
public class ScreenBackgroundChannelTests : IAsyncDisposable
{
    private readonly IScreenClient _client = Substitute.For<IScreenClient>();
    private readonly StreamMediaPlayer _player = new(NullLogger<StreamMediaPlayer>.Instance);
    private readonly List<string> _sentToPage = [];
    private readonly List<IScreenState> _sentToHost = [];
    private readonly ScreenIpcController _controller;

    public ScreenBackgroundChannelTests()
    {
        _player.SendToBrowser = _sentToPage.Add;
        _client.SendStateAsync(Arg.Do<IScreenState>(_sentToHost.Add)).Returns(Task.CompletedTask);
        _controller = new ScreenIpcController(_client, _player, NullLogger<ScreenIpcController>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _controller.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private void Receive(IScreenCommand command)
        => _client.CommandReceived += Raise.EventWith(new ScreenCommandReceivedEventArgs { Command = command });

    [Fact]
    public void LoadBackground_SendsTheBedToThePage()
    {
        _player.LoadBackground("http://host/media/bed/stream.m3u8", autoPlay: true);

        Assert.Contains(_sentToPage, m => m.Contains("bg-load") && m.Contains("stream.m3u8"));
    }

    [Fact]
    public void LoadBackground_DoesNotTouchTheSongChannel()
    {
        _player.LoadBackground("http://host/media/bed/stream.m3u8", autoPlay: true);

        Assert.DoesNotContain(_sentToPage, m => m.Contains("\"load\""));
        Assert.Null(_player.Info);
    }

    [Fact]
    public void BackgroundVolume_IsSentSeparatelyFromTheSongVolume()
    {
        _player.BackgroundVolume = 0.25f;

        Assert.Contains(_sentToPage, m => m.Contains("bg-volume"));
        Assert.DoesNotContain(_sentToPage, m => m.Contains("\"volume\""));
        Assert.Equal(1f, _player.Volume);
    }

    [Fact]
    public void StopBackground_ClearsTheBedWithoutStoppingTheSong()
    {
        _player.LoadBackground("http://host/media/bed/stream.m3u8", autoPlay: true);
        _player.StopBackground(TimeSpan.FromSeconds(2));

        Assert.Null(_player.BackgroundUrl);
        Assert.False(_player.IsBackgroundPlaying);
        Assert.DoesNotContain(_sentToPage, m => m.Contains("\"stop\""));
    }

    // Routing this through "ended" would run the singer's performance to completion because a bed
    // track finished, which is the whole reason the page reports it separately.
    [Fact]
    public void BackgroundEndedFromThePage_RaisesBackgroundEndedNotPlaybackEnded()
    {
        var background = 0;
        var playback = 0;
        _player.BackgroundEnded += (_, _) => background++;
        _player.PlaybackEnded += (_, _) => playback++;

        Assert.True(_player.HandleBrowserMessage("""{"type":"bg-ended"}"""));

        Assert.Equal(1, background);
        Assert.Equal(0, playback);
    }

    [Fact]
    public void SongEndedFromThePage_DoesNotRaiseBackgroundEnded()
    {
        var background = 0;
        _player.BackgroundEnded += (_, _) => background++;

        Assert.True(_player.HandleBrowserMessage("""{"type":"ended"}"""));

        Assert.Equal(0, background);
    }

    [Fact]
    public void BackgroundEnded_ReportsBackgroundStateToTheHost()
    {
        _player.HandleBrowserMessage("""{"type":"bg-ended"}""");

        var state = Assert.IsType<ScreenBackgroundState>(Assert.Single(_sentToHost));
        Assert.True(state.HasEnded);
    }

    [Fact]
    public void ABackgroundCommand_RepliesWithBackgroundStateRatherThanPlaybackState()
    {
        Receive(new PlayBackgroundCommand());

        Assert.IsType<ScreenBackgroundState>(Assert.Single(_sentToHost));
    }

    [Fact]
    public void ASongCommand_StillRepliesWithPlaybackState()
    {
        Receive(new PauseCommand());

        Assert.IsType<ScreenPlaybackState>(Assert.Single(_sentToHost));
    }

    [Fact]
    public void LoadBackgroundCommand_ReachesThePlayer()
    {
        Receive(new LoadBackgroundCommand { StreamUrl = "http://host/media/bed/stream.m3u8" });

        Assert.Equal("http://host/media/bed/stream.m3u8", _player.BackgroundUrl);
    }

    [Fact]
    public void SetBackgroundVolumeCommand_ReachesThePlayer()
    {
        Receive(new SetBackgroundVolumeCommand { Volume = 0.3f });

        Assert.Equal(0.3f, _player.BackgroundVolume);
    }

    // Nothing on this screen can pick the next bed track or stop this one while the host is away,
    // so it is paused alongside the song rather than left playing to an empty desk.
    [Fact]
    public void LosingTheHost_PausesTheBedAsWellAsTheSong()
    {
        _player.LoadBackground("http://host/media/bed/stream.m3u8", autoPlay: true);
        _sentToPage.Clear();

        _player.SetHostLost(true);

        Assert.Contains(_sentToPage, m => m.Contains("bg-pause"));
        Assert.Contains(_sentToPage, m => m.Contains("\"pause\""));
        Assert.False(_player.IsBackgroundPlaying);
    }
}
