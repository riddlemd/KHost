using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Visualisations;
using KHost.IPC.SignalR.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

/// <summary>A performance's own background takes the venue playlist's place under its words: black
/// draws nothing, a look draws itself, and a look that does not resolve hands back to the playlist.</summary>
public class LocalScreenPerformanceBackgroundTests
{
    private static readonly Guid PlaylistId = Guid.NewGuid();
    private static readonly Guid VideoId = Guid.NewGuid();
    private const string VideoUrl = "http://host:5251/media/backdrops/abc";
    private static readonly DisplayLoad Stems = new() { Stems = [new(0, AudioTrackRole.Music, "http://host/m.ogg", 100)] };
    private static readonly DisplayLoad Stream = new() { StreamUrl = "http://host/s.m3u8" };

    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly Venue.VenueSettings _settings = new() { VisualisationPlaylistId = PlaylistId };
    private readonly IVisualisationPlaylistRepository _playlists = Substitute.For<IVisualisationPlaylistRepository>();
    private readonly IVisualiserPresetService _presets = Substitute.For<IVisualiserPresetService>();
    private readonly IOptionsMonitor<HlsMediaStreamService.ServiceOptions> _streamOptions = Substitute.For<IOptionsMonitor<HlsMediaStreamService.ServiceOptions>>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly ITimedLyricsService _timedLyrics = Substitute.For<ITimedLyricsService>();
    private readonly ISourcePictureProbe _probe = Substitute.For<ISourcePictureProbe>();
    private readonly ISongLevelsService _levels = Substitute.For<ISongLevelsService>();
    private readonly IStemStreamService _stems = Substitute.For<IStemStreamService>();
    private readonly IMediaService _library = Substitute.For<IMediaService>();
    private readonly IVideoBackdropService _backdrops = Substitute.For<IVideoBackdropService>();

    private readonly VisualisationPlaylist _playlist = new()
    {
        Id = PlaylistId,
        Name = "Night",
        Entries =
        [
            new() { Id = Guid.NewGuid(), PresetName = "Rovastar - Oozing Resistance" },
            new() { Id = Guid.NewGuid(), PresetName = "_Mig_049" },
        ],
    };

    public LocalScreenPerformanceBackgroundTests()
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = _settings });
        _playback.CurrentProgram.Returns(new PlaybackProgram.Idle());
        _stems.ResolveStemInput(Arg.Any<string>()).Returns(call => "/disk/" + call.Arg<string>()[(call.Arg<string>().LastIndexOf('/') + 1)..]);
        _stems.StemsMixedInto(Arg.Any<string>()).Returns((IReadOnlyList<StemSource>?)null);
        _levels.Begin(Arg.Any<IReadOnlyList<SongLevelsInput>>()).Returns("http://host/media/levels/1");
        _playlists.ReadWithEntriesAsync(PlaylistId).Returns(_ => _playlist);
        _playlists.ReadAsync(PlaylistId).Returns(_ => _playlist);
        _presets.ReadAll().Returns([]);
        _streamOptions.CurrentValue.Returns(new HlsMediaStreamService.ServiceOptions { BaseAddress = "http://host:5251/" });
        _library.ReadAsync(VideoId).Returns(new Media { Title = "Waves", FilePath = "/videos/waves.mkv", Type = MediaType.Video });
    }

    private LocalScreenDisplayProvider Provider()
    {
        var services = new ServiceCollection()
            .AddSingleton(_playback)
            .AddSingleton(_performances)
            .AddSingleton(_timedLyrics)
            .AddSingleton(_levels)
            .AddSingleton(_stems)
            .AddSingleton<IVisualisationPlaylistService>(new VisualisationPlaylistService(
                NullLogger<VisualisationPlaylistService>.Instance, _playlists, _broker))
            .AddSingleton(_presets)
            .AddSingleton(_streamOptions)
            .AddSingleton(_library)
            .AddSingleton(_backdrops)
            .AddSingleton(_probe);

        return new LocalScreenDisplayProvider(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [], _broker, _venues,
            services: services.BuildServiceProvider(), redrawSettle: TimeSpan.Zero);
    }

    /// <summary>A song with timed words whose row, read afresh, carries <paramref name="background"/>;
    /// the program's own copy carries none, as a load-time snapshot taken before an edit would.</summary>
    private PlaybackProgram.Playing Playing(PerformanceBackground? background, string path = "/songs/africa.song")
    {
        var performance = new Performance { Id = Guid.NewGuid() };
        _performances.ReadAsync(performance.Id).Returns(new Performance { Id = performance.Id, Background = background });

        var song = new PlaybackProgram.Playing(new Media { Title = "Africa", FilePath = path }, performance);
        _timedLyrics.GetTimedLyricsAsync(path, Arg.Any<CancellationToken>()).Returns(new TimedLyrics
        {
            DurationSeconds = 90,
            Bounds = new LyricBox(0, 0, 640, 360),
            Pages = [new LyricPage { ShowFromSeconds = 1, ShowUntilSeconds = 5 }],
        });
        _playback.CurrentProgram.Returns(song);

        return song;
    }

    private static PerformanceBackground Look(string preset = "Flexi - mindblob", VisualiserPresetSource source = VisualiserPresetSource.Bundled) => new()
    {
        Type = PerformanceBackgroundType.Look,
        PresetSource = source,
        PresetName = preset,
        Brightness = 120,
        Saturation = 60,
        Sensitivity = 250,
    };

    private static PerformanceBackground VideoLook() => new()
    {
        Type = PerformanceBackgroundType.Look,
        PresetSource = VisualiserPresetSource.Video,
        VideoMediaId = VideoId,
        Brightness = 70,
        Saturation = 40,
    };

    private List<SetVisualiserCommand> Sent()
        => [.. _screenServer.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<SetVisualiserCommand>()];

    private SetVisualiserCommand Last() => Sent().Last();

    [Fact]
    public async Task LoadAsync_ABlackBackground_TurnsItOffAtAVenueWithAPlaylist()
    {
        Playing(new PerformanceBackground { Type = PerformanceBackgroundType.Black });
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.False(Last().Enabled);
    }

    [Fact]
    public async Task LoadAsync_ALook_DrawsItWithItsSettingsAndTheSongsLevels()
    {
        Playing(Look());
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        var sent = Last();
        Assert.True(sent.Enabled);
        Assert.Equal(("Flexi - mindblob", 120, 60, 250), (sent.PresetName, sent.Brightness, sent.Saturation, sent.Sensitivity));
        Assert.Equal("http://host/media/levels/1", sent.LevelsUrl);
    }

    /// <summary>The look is the song's own, so the venue's rotation is where it was for the next song.</summary>
    [Fact]
    public async Task LoadAsync_ALook_TakesNoTurnFromTheVenuesPlaylist()
    {
        Playing(Look());
        using var provider = Provider();
        await provider.LoadAsync(Stems);

        Playing(null, "/songs/rosanna.song");
        await provider.LoadAsync(Stems);

        Assert.Equal("Rovastar - Oozing Resistance", Last().PresetName);
    }

    [Fact]
    public async Task LoadAsync_ALookAtAVenueWithNoPlaylist_DrawsIt()
    {
        _settings.VisualisationPlaylistId = null;
        Playing(Look());
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.True(Last().Enabled);
        Assert.Equal("Flexi - mindblob", Last().PresetName);
    }

    [Fact]
    public async Task LoadAsync_NoBackgroundAtAVenueWithNoPlaylist_LeavesBlackWithoutReadingTheFile()
    {
        _settings.VisualisationPlaylistId = null;
        Playing(null, "/songs/africa.mp4");
        using var provider = Provider();

        await provider.LoadAsync(Stream);

        Assert.False(Last().Enabled);
        await _probe.DidNotReceiveWithAnyArgs().HasMovingPictureAsync(default!, default);
    }

    /// <summary>A song's own picture is never covered, whatever its background says.</summary>
    [Fact]
    public async Task LoadAsync_ALookOnASongWithItsOwnPicture_KeepsThePicture()
    {
        Playing(Look(), "/songs/africa.mp4");
        _probe.HasMovingPictureAsync("/songs/africa.mp4", Arg.Any<CancellationToken>()).Returns(true);
        using var provider = Provider();

        await provider.LoadAsync(Stream);

        Assert.False(Last().Enabled);
    }

    [Fact]
    public async Task LoadAsync_ALookWhosePresetIsGone_DrawsTheVenuesPlaylist()
    {
        Playing(Look("Deleted Swirl", VisualiserPresetSource.Imported));
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.True(Last().Enabled);
        Assert.Equal("Rovastar - Oozing Resistance", Last().PresetName);
    }

    /// <summary>A rebuild of the same song keeps the playlist entry it fell back to.</summary>
    [Fact]
    public async Task LoadAsync_TheSameFallenBackSongAgain_KeepsItsPlaylistEntry()
    {
        Playing(Look("Deleted Swirl", VisualiserPresetSource.Imported));
        using var provider = Provider();
        await provider.LoadAsync(Stems);

        await provider.LoadAsync(Stream);

        Assert.Equal(["Rovastar - Oozing Resistance", "Rovastar - Oozing Resistance"], Sent().Select(c => c.PresetName));
    }

    [Fact]
    public async Task LoadAsync_AVideoLook_SendsOffAtOnceThenTheVideo()
    {
        var url = new TaskCompletionSource<string?>();
        _backdrops.UrlForAsync(Arg.Any<Media>(), Arg.Any<CancellationToken>()).Returns(url.Task);
        Playing(VideoLook());
        using var provider = Provider();

        await provider.LoadAsync(Stems);
        Assert.False(Last().Enabled);

        url.SetResult(VideoUrl);

        Assert.True(await WaitUntilAsync(() => Last().VideoUrl == VideoUrl));
        Assert.Equal((70, 40), (Last().Brightness, Last().Saturation));
    }

    /// <summary>A key change reloads the same song; its video is not looked up again.</summary>
    [Fact]
    public async Task LoadAsync_TheSameVideoLookSongAgain_SendsTheVideoWithoutAskingAgain()
    {
        _backdrops.UrlForAsync(Arg.Any<Media>(), Arg.Any<CancellationToken>()).Returns(VideoUrl);
        Playing(VideoLook());
        using var provider = Provider();
        await provider.LoadAsync(Stems);
        Assert.True(await WaitUntilAsync(() => Last().VideoUrl == VideoUrl));

        await provider.LoadAsync(Stream);

        Assert.Equal(VideoUrl, Last().VideoUrl);
        await _backdrops.Received(1).UrlForAsync(Arg.Any<Media>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_AVideoLookTheScreenCannotPlay_DrawsTheVenuesPlaylist()
    {
        _backdrops.UrlForAsync(Arg.Any<Media>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        Playing(VideoLook());
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.True(await WaitUntilAsync(() => Last().PresetName == "Rovastar - Oozing Resistance"));
    }

    [Fact]
    public async Task LoadAsync_AVideoLookNamingARowThatIsGone_DrawsTheVenuesPlaylist()
    {
        _library.ReadAsync(VideoId).Returns((Media?)null);
        Playing(VideoLook());
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.True(await WaitUntilAsync(() => Last().PresetName == "Rovastar - Oozing Resistance"));
        Assert.Empty(_backdrops.ReceivedCalls());
    }

    [Fact]
    public async Task LoadAsync_AVideoLookNamingNoVideo_DrawsTheVenuesPlaylist()
    {
        var look = VideoLook();
        look.VideoMediaId = null;
        Playing(look);
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.Equal("Rovastar - Oozing Resistance", Last().PresetName);
    }

    /// <summary>The card between singers is the venue's, never the song's.</summary>
    [Fact]
    public async Task NextSingerAnnounced_AfterALookSong_TakesTheVenuesPlaylistEntry()
    {
        _settings.NextSingerBackground = NextSingerBackground.Visualisation;
        Playing(Look());
        using var provider = Provider();
        await provider.LoadAsync(Stems);

        await _broker.PublishAsync(new NextSingerAnnounced(new NextSingerCard { Singer = "Ada", Song = "Today" }));

        var card = _screenServer.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<ShowNextSingerCommand>()
            .Single();
        Assert.Equal("Rovastar - Oozing Resistance", card.Visualiser!.PresetName);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int attempts = 500)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return false;
    }
}
