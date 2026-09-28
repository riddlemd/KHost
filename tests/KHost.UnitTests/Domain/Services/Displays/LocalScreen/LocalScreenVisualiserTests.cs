using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Visualisations;
using KHost.Abstractions.Repositories;
using Microsoft.Extensions.Options;
using KHost.IPC.SignalR.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

/// <summary>The visualiser stands in for black and nothing else: a performance with timed words and
/// no picture of its own, at a venue whose visualisation playlist has something in it.</summary>
public class LocalScreenVisualiserTests
{
    private static readonly Guid PlaylistId = Guid.NewGuid();

    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly Venue.VenueSettings _settings = new() { VisualisationPlaylistId = PlaylistId };
    private readonly IVisualisationPlaylistRepository _playlists = Substitute.For<IVisualisationPlaylistRepository>();
    private readonly IVisualiserPresetService _presets = Substitute.For<IVisualiserPresetService>();
    private readonly IOptionsMonitor<HlsMediaStreamService.ServiceOptions> _streamOptions = Substitute.For<IOptionsMonitor<HlsMediaStreamService.ServiceOptions>>();
    private readonly SequenceRandom _random = new();
    private VisualisationPlaylist _playlist = new()
    {
        Id = PlaylistId,
        Name = "Night",
        Entries =
        [
            new() { PresetName = "Rovastar - Oozing Resistance", Brightness = 80, Saturation = 150, Sensitivity = 200, DarkenBehindWords = true },
            new() { PresetName = "_Mig_049", DarkenBehindWords = false },
        ],
    };
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly ITimedLyricsService _timedLyrics = Substitute.For<ITimedLyricsService>();
    private readonly ISourcePictureProbe _probe = Substitute.For<ISourcePictureProbe>();
    private readonly ISongLevelsService _levels = Substitute.For<ISongLevelsService>();
    private readonly IStemStreamService _stems = Substitute.For<IStemStreamService>();
    private int _reads;

    private static readonly DisplayLoad Stems = new() { Stems = [new(0, AudioTrackRole.Music, "http://host/m.ogg", 100)] };
    private static readonly DisplayLoad Stream = new() { StreamUrl = "http://host/s.m3u8" };

    public LocalScreenVisualiserTests()
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = _settings });
        _playback.CurrentProgram.Returns(new PlaybackProgram.Idle());
        _levels.Begin(Arg.Any<IReadOnlyList<SongLevelsInput>>()).Returns(_ => $"http://host/media/levels/{++_reads}");
        _stems.ResolveStemInput(Arg.Any<string>()).Returns(call => "/disk/" + call.Arg<string>()[(call.Arg<string>().LastIndexOf('/') + 1)..]);
        _stems.StemsMixedInto(Arg.Any<string>()).Returns((IReadOnlyList<StemSource>?)null);
        _playlists.ReadWithEntriesAsync(PlaylistId).Returns(_ => _playlist);
        _playlists.ReadAsync(PlaylistId).Returns(_ => _playlist);
        _presets.ReadAll().Returns([]);
        _streamOptions.CurrentValue.Returns(new HlsMediaStreamService.ServiceOptions { BaseAddress = "http://host:5251/" });
    }

    private LocalScreenDisplayProvider Provider(bool withProbe = true)
    {
        var services = new ServiceCollection()
            .AddSingleton(_playback)
            .AddSingleton(_timedLyrics)
            .AddSingleton(_levels)
            .AddSingleton(_stems)
            .AddSingleton<IVisualisationPlaylistService>(new VisualisationPlaylistService(
                NullLogger<VisualisationPlaylistService>.Instance, _playlists, _broker, _random))
            .AddSingleton(_presets)
            .AddSingleton(_streamOptions);

        if (withProbe) services.AddSingleton(_probe);

        return new LocalScreenDisplayProvider(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [], _broker, _venues,
            services: services.BuildServiceProvider(), redrawSettle: TimeSpan.Zero);
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
    public async Task LoadAsync_StemsWithTimedWords_TurnsItOnWithThePlaylistsFirstEntry()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.True(Last().Enabled);
        Assert.Equal("Rovastar - Oozing Resistance", Last().PresetName);
        Assert.Null(Last().PresetUrl);
    }

    /// <summary>Everything the entry says about how it is drawn reaches the screen.</summary>
    [Fact]
    public async Task LoadAsync_SendsTheEntrysSettings()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.Equal((80, 150, 200, true), (Last().Brightness, Last().Saturation, Last().Sensitivity, Last().DarkenLyricBands));
    }

    [Fact]
    public async Task LoadAsync_TheVenueHasNoPlaylist_LeavesBlack()
    {
        _settings.VisualisationPlaylistId = null;
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.False(Last().Enabled);
    }

    [Fact]
    public async Task LoadAsync_AnEmptyPlaylist_LeavesBlack()
    {
        _playlist = new VisualisationPlaylist { Id = PlaylistId, Name = "Empty" };
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.False(Last().Enabled);
    }

    /// <summary>A playlist deleted after a venue picked it leaves the id behind.</summary>
    [Fact]
    public async Task LoadAsync_APlaylistThatIsGone_LeavesBlack()
    {
        _settings.VisualisationPlaylistId = Guid.NewGuid();
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.False(Last().Enabled);
    }

    [Fact]
    public async Task LoadAsync_AnImportedPreset_SendsWhereToFetchIt()
    {
        var written = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        _playlist.Entries[0].PresetSource = VisualiserPresetSource.Imported;
        _playlist.Entries[0].PresetName = "My Swirl";
        _presets.ReadAll().Returns([new VisualiserPreset { Name = "My Swirl", Source = VisualiserPresetSource.Imported, ImportedUtc = written }]);
        Playing("/songs/africa.song");
        using var provider = Provider();

        await provider.LoadAsync(Stems);

        Assert.True(Last().Enabled);
        Assert.Null(Last().PresetName);
        Assert.Equal($"http://host:5251/media/visualiser-presets/My%20Swirl?v={written.Ticks}", Last().PresetUrl);
    }

    [Fact]
    public async Task LoadAsync_AnImportedPresetSinceDeleted_LeavesBlack()
    {
        _playlist.Entries[0].PresetSource = VisualiserPresetSource.Imported;
        _playlist.Entries[0].PresetName = "Gone";
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
    public async Task LoadAsync_TheSameProgramAgain_KeepsTheEntryAndProbesOnce()
    {
        _probe.HasMovingPictureAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        Playing("/songs/africa.mp4");
        using var provider = Provider();

        await provider.LoadAsync(Stream);
        await provider.LoadAsync(Stream);

        Assert.Equal(["Rovastar - Oozing Resistance", "Rovastar - Oozing Resistance"], Sent().Select(c => c.PresetName));
        await _probe.Received(1).HasMovingPictureAsync("/songs/africa.mp4", Arg.Any<CancellationToken>());
    }

    /// <summary>In order, one entry per song, wrapping at the end.</summary>
    [Fact]
    public async Task LoadAsync_EachNextSong_TakesTheNextEntryInOrder()
    {
        using var provider = Provider();

        foreach (var song in new[] { "africa", "rosanna", "hold-the-line" })
        {
            Playing($"/songs/{song}.song");
            await provider.LoadAsync(Stems);
        }

        Assert.Equal(["Rovastar - Oozing Resistance", "_Mig_049", "Rovastar - Oozing Resistance"], Sent().Select(c => c.PresetName));
    }

    /// <summary>A song that shows its own picture does not use up a turn.</summary>
    [Fact]
    public async Task LoadAsync_ASongWithItsOwnPicture_DoesNotAdvanceTheTurn()
    {
        using var provider = Provider();
        Playing("/songs/africa.song");
        await provider.LoadAsync(Stems);

        Playing("/songs/video.mp4");
        _probe.HasMovingPictureAsync("/songs/video.mp4", Arg.Any<CancellationToken>()).Returns(true);
        await provider.LoadAsync(Stream);

        Playing("/songs/rosanna.song");
        await provider.LoadAsync(Stems);

        Assert.Equal(["Rovastar - Oozing Resistance", null, "_Mig_049"], Sent().Select(c => c.PresetName));
    }

    [Fact]
    public async Task LoadAsync_AShuffledPlaylist_NeverRepeatsTheEntryJustShown()
    {
        _playlist.Shuffle = true;
        _playlist.Entries.Add(new VisualisationEntry { PresetName = "Aderrasi - Potion of Spirits" });

        // Each draw asks for the lowest choice: the first song gets entry 0, and every later one,
        // with the last pick taken out, the lowest of what is left.
        _random.Values.Enqueue(0);
        _random.Values.Enqueue(0);
        _random.Values.Enqueue(0);
        using var provider = Provider();

        foreach (var song in new[] { "africa", "rosanna", "hold-the-line" })
        {
            Playing($"/songs/{song}.song");
            await provider.LoadAsync(Stems);
        }

        Assert.Equal(["Rovastar - Oozing Resistance", "_Mig_049", "Rovastar - Oozing Resistance"], Sent().Select(c => c.PresetName));
    }

    [Fact]
    public async Task SelectedVenueChanged_MidSong_TurnsItOnWithoutAReload()
    {
        _settings.VisualisationPlaylistId = null;
        Playing("/songs/africa.song");
        using var provider = Provider();
        await provider.LoadAsync(Stems);

        _settings.VisualisationPlaylistId = PlaylistId;
        _broker.Announce(new SelectedVenueChanged());

        Assert.True(await WaitUntilAsync(() => Sent().Any(c => c.Enabled)));
    }

    /// <summary>A setting moved on the Visualisations page reaches the song on screen.</summary>
    [Fact]
    public async Task VisualisationPlaylistsChanged_MidSong_SendsTheEntrysNewSettings()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();
        await provider.LoadAsync(Stems);

        _playlist.Entries[0].Brightness = 120;
        _broker.Announce(new VisualisationPlaylistsChanged());

        Assert.True(await WaitUntilAsync(() => Sent().Count == 2 && Last().Brightness == 120));
        Assert.Equal("Rovastar - Oozing Resistance", Last().PresetName);
    }

    /// <summary>An edit re-reads this song's own entry, not the playlist's first, and does not
    /// advance the rotation.</summary>
    [Fact]
    public async Task VisualisationPlaylistsChanged_OnTheSecondSong_KeepsThatSongsEntry()
    {
        using var provider = Provider();
        Playing("/songs/africa.song");
        await provider.LoadAsync(Stems);
        Playing("/songs/rosanna.song");
        await provider.LoadAsync(Stems);

        _playlist.Entries[1].Saturation = 30;
        _broker.Announce(new VisualisationPlaylistsChanged());

        Assert.True(await WaitUntilAsync(() => Sent().Count == 3));
        Assert.Equal(("_Mig_049", 30), (Last().PresetName, Last().Saturation));
    }

    /// <summary>The entry on screen was removed: the song moves on to the playlist's next.</summary>
    [Fact]
    public async Task VisualisationPlaylistsChanged_TheEntryOnScreenRemoved_PicksAnother()
    {
        Playing("/songs/africa.song");
        using var provider = Provider();
        await provider.LoadAsync(Stems);

        _playlist = new VisualisationPlaylist { Id = PlaylistId, Name = "Night", Entries = [_playlist.Entries[1]] };
        _broker.Announce(new VisualisationPlaylistsChanged());

        Assert.True(await WaitUntilAsync(() => Sent().Count == 2 && Last().PresetName == "_Mig_049"));
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

    /// <summary>The entry's own switch, per song.</summary>
    [Fact]
    public async Task LoadAsync_AnEntryWithTheBandsOff_SendsThemOff()
    {
        _playlist.Entries[0].DarkenBehindWords = false;
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
        _settings.VisualisationPlaylistId = null;
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

    /// <summary>Hands out queued values, so a shuffle's picks are known in advance.</summary>
    private sealed class SequenceRandom : Random
    {
        public Queue<int> Values { get; } = new();

        public override int Next(int maxValue) => Values.Count > 0 ? Math.Min(Values.Dequeue(), maxValue - 1) : 0;
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
