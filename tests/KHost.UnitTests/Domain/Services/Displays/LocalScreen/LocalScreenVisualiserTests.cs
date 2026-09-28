using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Domain.Services.Messaging;
using KHost.IPC.SignalR.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

/// <summary>The visualiser stands in for black and nothing else: a performance with timed words and
/// no picture of its own, at a venue that asked for one.</summary>
public class LocalScreenVisualiserTests
{
    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly Venue.VenueSettings _settings = new() { SongVisualiserEnabled = true };
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly ITimedLyricsService _timedLyrics = Substitute.For<ITimedLyricsService>();
    private readonly ISourcePictureProbe _probe = Substitute.For<ISourcePictureProbe>();
    private int _picks;

    private static readonly DisplayLoad Stems = new() { Stems = [new(0, AudioTrackRole.Music, "http://host/m.ogg", 100)] };
    private static readonly DisplayLoad Stream = new() { StreamUrl = "http://host/s.m3u8" };

    public LocalScreenVisualiserTests()
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = _settings });
        _playback.CurrentProgram.Returns(new PlaybackProgram.Idle());
    }

    private LocalScreenDisplayProvider Provider(bool withProbe = true)
    {
        var services = new ServiceCollection()
            .AddSingleton(_playback)
            .AddSingleton(_timedLyrics);

        if (withProbe) services.AddSingleton(_probe);

        return new LocalScreenDisplayProvider(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [], _broker, _venues,
            services: services.BuildServiceProvider(), redrawSettle: TimeSpan.Zero,
            pickPreset: () => 40 + _picks++);
    }

    private PlaybackProgram.Playing Playing(string path, bool words = true, bool pages = true, bool performance = true)
    {
        var song = new PlaybackProgram.Playing(new Media { Title = "Africa", FilePath = path }, performance ? new Performance() : null);

        TimedLyrics? lyrics = !words ? null : new TimedLyrics
        {
            DurationSeconds = 90,
            Bounds = new LyricBox(0, 0, 640, 360),
            Pages = pages ? [new LyricPage { ShowFromSeconds = 1, ShowUntilSeconds = 5 }] : [],
        };
        _timedLyrics.GetTimedLyricsAsync(path, Arg.Any<CancellationToken>()).Returns(lyrics);
        _playback.CurrentProgram.Returns(song);

        return song;
    }

    private List<SetVisualiserCommand> Sent()
        => [.. _screenServer.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<SetVisualiserCommand>()];

    private SetVisualiserCommand Last() => Sent().Last();

    [Fact]
    public async Task LoadAsync_StemsWithTimedWords_TurnsItOnWithThePickedPreset()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.True(Last().Enabled);
        Assert.Equal(40, Last().Preset);
    }

    [Fact]
    public async Task LoadAsync_TheVenueHasItOff_LeavesBlack()
    {
        _settings.SongVisualiserEnabled = false;
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.False(Last().Enabled);
    }

    /// <summary>A venue stored before the setting existed has no key for it, which reads as off.</summary>
    [Fact]
    public async Task LoadAsync_AVenueThatWasNeverAsked_LeavesBlack()
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "Old", Settings = new Venue.VenueSettings() });
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.False(Last().Enabled);
    }

    [Fact]
    public async Task LoadAsync_ASongWithNoTimedWords_LeavesItsOwnPicture()
    {
        Playing("/songs/africa.song", words: false);
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.False(Last().Enabled);
    }

    [Fact]
    public async Task LoadAsync_TimedWordsWithNoPages_LeavesBlack()
    {
        Playing("/songs/africa.song", pages: false);
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.False(Last().Enabled);
    }

    [Fact]
    public async Task LoadAsync_AnAdClip_NeverGetsOne()
    {
        Playing("/ads/beer.mp4", performance: false);
        using var provider = Provider();

        await provider.LoadAsync(Stream);

        Assert.False(Last().Enabled);
    }

    /// <summary>Nothing from an audio file is shown, cover art included, so there is nothing to ask.</summary>
    [Fact]
    public async Task LoadAsync_AnEncodedAudioFile_TurnsItOnWithoutProbing()
    {
        Playing("/songs/africa.mp3");
        _probe.HasMovingPictureAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        using var provider = Provider();

        await provider.LoadAsync(Stream);

        Assert.True(Last().Enabled);
        await _probe.DidNotReceiveWithAnyArgs().HasMovingPictureAsync(default!, default);
    }

    [Fact]
    public async Task LoadAsync_AVideoWithItsOwnPicture_KeepsThePicture()
    {
        Playing("/songs/africa.mp4");
        _probe.HasMovingPictureAsync("/songs/africa.mp4", Arg.Any<CancellationToken>()).Returns(true);
        using var provider = Provider();

        await provider.LoadAsync(Stream);

        Assert.False(Last().Enabled);
    }

    [Fact]
    public async Task LoadAsync_AContainerWithNoMovingPicture_TurnsItOn()
    {
        Playing("/songs/africa.mp4");
        _probe.HasMovingPictureAsync("/songs/africa.mp4", Arg.Any<CancellationToken>()).Returns(false);
        using var provider = Provider();

        await provider.LoadAsync(Stream);

        Assert.True(Last().Enabled);
    }

    /// <summary>A CD+G's graphics are its picture, and the words are in them.</summary>
    [Fact]
    public async Task LoadAsync_ACdg_KeepsItsGraphics()
    {
        Playing("/songs/africa.cdg");
        _probe.HasMovingPictureAsync("/songs/africa.cdg", Arg.Any<CancellationToken>()).Returns(true);
        using var provider = Provider();

        await provider.LoadAsync(Stream);

        Assert.False(Last().Enabled);
    }

    /// <summary>Unable to ask, it keeps whatever the file shows rather than drawing over a video.</summary>
    [Fact]
    public async Task LoadAsync_NoProbeToAsk_KeepsThePicture()
    {
        Playing("/songs/africa.mp4");
        using var provider = Provider(withProbe: false);

        await provider.LoadAsync(Stream);

        Assert.False(Last().Enabled);
    }

    /// <summary>A rebuild at a new key reloads the same program; the picture must not jump.</summary>
    [Fact]
    public async Task LoadAsync_TheSameProgramAgain_KeepsThePresetAndProbesOnce()
    {
        Playing("/songs/africa.mp4");
        using var provider = Provider();

        await provider.LoadAsync(Stream);
        await provider.LoadAsync(Stream);

        Assert.Equal([40, 40], Sent().Select(c => c.Preset));
        Assert.Equal(1, _picks);
        await _probe.Received(1).HasMovingPictureAsync("/songs/africa.mp4", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_TheNextSong_PicksAgain()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();
        await provider.LoadAsync(Stems);

        Playing("/songs/rosanna.song");
        await provider.LoadAsync(Stems);

        Assert.Equal([40, 41], Sent().Select(c => c.Preset));
    }

    [Fact]
    public async Task SelectedVenueChanged_MidSong_TurnsItOnWithoutAReload()
    {
        _settings.SongVisualiserEnabled = false;
        Playing("/songs/africa.song");
        using var provider = Provider();
        await provider.LoadAsync(Stems);

        _settings.SongVisualiserEnabled = true;
        _broker.Announce(new SelectedVenueChanged());

        Assert.True(await WaitUntilAsync(() => Sent().Any(c => c.Enabled)));
    }

    /// <summary>A venue edit before this song has loaded must not decide it on the last song's load.</summary>
    [Fact]
    public async Task SelectedVenueChanged_BeforeTheNextSongLoads_SendsNothing()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();
        await provider.LoadAsync(Stems);

        Playing("/songs/rosanna.song");
        _broker.Announce(new SelectedVenueChanged());
        await Task.Delay(150);

        Assert.Single(Sent());
    }

    /// <summary>Stems are sound only, whatever else the file carries, and the screen mixes them
    /// rather than playing the stream sent beside them.</summary>
    [Fact]
    public async Task LoadAsync_StemsOfAFileWithAPicture_TurnsItOn()
    {
        Playing("/songs/africa.mp4");
        _probe.HasMovingPictureAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        using var provider = Provider();

        await provider.LoadAsync(new DisplayLoad { StreamUrl = "http://host/s.m3u8", Stems = Stems.Stems });

        Assert.True(Last().Enabled);
    }

    [Fact]
    public async Task PlaybackChanged_ToAnAdStill_TakesItDown()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();
        await provider.LoadAsync(Stems);

        _playback.CurrentProgram.Returns(new PlaybackProgram.AdStill("http://host/media/image/ad", ImageScaling.Fill));
        _broker.Announce(new PlaybackChanged());

        Assert.True(await WaitUntilAsync(() => Sent().Count == 2 && !Last().Enabled));
    }

    [Fact]
    public async Task PlaybackChanged_ToIdle_TakesItDown()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();
        await provider.LoadAsync(Stems);

        _playback.CurrentProgram.Returns(new PlaybackProgram.Idle());
        _broker.Announce(new PlaybackChanged());

        Assert.True(await WaitUntilAsync(() => Sent().Count == 2 && !Last().Enabled));
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return false;
    }
}
