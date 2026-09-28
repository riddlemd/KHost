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
    private readonly ISongLevelsService _levels = Substitute.For<ISongLevelsService>();
    private readonly IStemStreamService _stems = Substitute.For<IStemStreamService>();
    private int _reads;
    private int _picks;

    private static readonly DisplayLoad Stems = new() { Stems = [new(0, AudioTrackRole.Music, "http://host/m.ogg", 100)] };
    private static readonly DisplayLoad Stream = new() { StreamUrl = "http://host/s.m3u8" };

    public LocalScreenVisualiserTests()
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = _settings });
        _playback.CurrentProgram.Returns(new PlaybackProgram.Idle());
        _levels.Begin(Arg.Any<IReadOnlyList<SongLevelsInput>>()).Returns(_ => $"http://host/media/levels/{++_reads}");
        _stems.ResolveStemInput(Arg.Any<string>()).Returns(call => "/disk/" + call.Arg<string>()[(call.Arg<string>().LastIndexOf('/') + 1)..]);
        _stems.StemsMixedInto(Arg.Any<string>()).Returns((IReadOnlyList<StemSource>?)null);
    }

    private LocalScreenDisplayProvider Provider(bool withProbe = true)
    {
        var services = new ServiceCollection()
            .AddSingleton(_playback)
            .AddSingleton(_timedLyrics)
            .AddSingleton(_levels)
            .AddSingleton(_stems);

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

    // --- the dark band behind the words ---

    /// <summary>The screen can draw it, but nothing supplies it yet: a visualisation's own settings
    /// will, per song.</summary>
    [Fact]
    public async Task LoadAsync_TheVisualiserOn_SendsTheWordsBandOff()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.True(Last().Enabled);
        Assert.False(Last().DarkenLyricBands);
    }

    // --- the host's levels ---

    [Fact]
    public async Task LoadAsync_ItIsOn_SendsWhereTheSongsLevelsAreServed_ReadFromTheStemsOnDisk()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.Equal("http://host/media/levels/1", Last().LevelsUrl);
        _levels.Received(1).Begin(Arg.Is<IReadOnlyList<SongLevelsInput>>(inputs =>
            inputs.SequenceEqual(new[] { new SongLevelsInput("/disk/m.ogg", 100) })));
    }

    [Fact]
    public async Task LoadAsync_TheVenueHasItOff_ReadsNoLevels()
    {
        _settings.SongVisualiserEnabled = false;
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.Null(Last().LevelsUrl);
        _levels.DidNotReceiveWithAnyArgs().Begin(default!);
    }

    /// <summary>A key change reloads the same program as an encode; the levels are song time, so
    /// the stems' levels still hold and are not read again.</summary>
    [Fact]
    public async Task LoadAsync_TheSameProgramReKeyed_KeepsItsLevels()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);
        await provider.LoadAsync(Stream);

        Assert.Equal(["http://host/media/levels/1", "http://host/media/levels/1"], Sent().Select(c => c.LevelsUrl));
        _levels.ReceivedWithAnyArgs(1).Begin(default!);
    }

    [Fact]
    public async Task LoadAsync_TheNextSong_ReadsItsOwnLevels()
    {
        Playing("/songs/africa.mp3");
        using var provider = Provider();
        await provider.LoadAsync(Stream);

        Playing("/songs/rosanna.mp3");
        await provider.LoadAsync(Stream);

        Assert.Equal(["http://host/media/levels/1", "http://host/media/levels/2"], Sent().Select(c => c.LevelsUrl));
        _levels.Received(1).Begin(Arg.Is<IReadOnlyList<SongLevelsInput>>(inputs => inputs.Single().Input == "/songs/rosanna.mp3"));
    }

    /// <summary>Still drawn, just without the beat: a provider's own container with no stems is
    /// nothing ffmpeg opens.</summary>
    [Fact]
    public async Task LoadAsync_NothingTheHostCanRead_TurnsItOnWithNoLevels()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stream);

        Assert.True(Last().Enabled);
        Assert.Null(Last().LevelsUrl);
        _levels.DidNotReceiveWithAnyArgs().Begin(default!);
    }

    /// <summary>A stem song whose first load is already re-keyed arrives as the host's own mix, with
    /// no stems in the load; the stems behind that mix are read instead.</summary>
    [Fact]
    public async Task LoadAsync_AStemSongLoadedReKeyed_ReadsTheStemsTheHostMixed()
    {
        _stems.StemsMixedInto(Stream.StreamUrl!).Returns(
            [new StemSource(0, AudioTrackRole.Music, "http://host/media/s/m.ogg", 100), new StemSource(1, AudioTrackRole.Backing, "http://host/media/s/b.ogg", 70)]);
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stream);

        Assert.Equal("http://host/media/levels/1", Last().LevelsUrl);
        _levels.Received(1).Begin(Arg.Is<IReadOnlyList<SongLevelsInput>>(inputs =>
            inputs.SequenceEqual(new[] { new SongLevelsInput("/disk/m.ogg", 100), new SongLevelsInput("/disk/b.ogg", 70) })));
    }

    [Fact]
    public async Task LoadAsync_AStemTheStreamServiceDoesNotKnow_TurnsItOnWithNoLevels()
    {
        _stems.ResolveStemInput(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("not a session file"));
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.True(Last().Enabled);
        Assert.Null(Last().LevelsUrl);
    }

    [Fact]
    public async Task PlaybackChanged_ToIdle_DropsTheLevels()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();
        await provider.LoadAsync(Stems);
        _levels.DidNotReceive().Clear();

        _playback.CurrentProgram.Returns(new PlaybackProgram.Idle());
        _broker.Announce(new PlaybackChanged());

        Assert.True(await WaitUntilAsync(() => _levels.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(ISongLevelsService.Clear))));
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
