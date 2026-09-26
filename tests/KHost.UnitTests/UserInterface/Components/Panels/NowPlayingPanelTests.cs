using Microsoft.Extensions.Logging.Abstractions;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Domain.Services.Messaging;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

public class NowPlayingPanelTests : BunitContext
{
    private const string ArtistSelector = ".kh-now-playing__artist";

    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly ITimedLyricsService _lyrics = Substitute.For<ITimedLyricsService>();

    public NowPlayingPanelTests()
    {
        // The panel calls into JS to build a seek bar on first render; none of it matters here.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _playback.State.Returns(PlaybackState.Playing);
        _playback.Position.Returns(TimeSpan.Zero);

        // The header hosts SongControls, which reads the control shape from the machine settings;
        // without one registered every render of this panel throws.
        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        // The panel reads the venue's say on whether a name queued with a song is honoured.

        var venues = Substitute.For<IVenuesService>();

        venues.ReadSelectedVenueAsync().Returns((Venue?)null);

        Services.AddSingleton(venues);

        Services.AddSingleton(_playback);

        Services.AddSingleton(Substitute.For<INextSingerCardService>());

        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(appSettings);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(Substitute.For<ISingerQueueService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(_lyrics);

        // The break music controls ride this panel's header, so their services have to resolve
        // even in tests that only care about the song; naming no provider keeps the bar out of scope.
        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(Substitute.For<IAdService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
    }

    /// <summary>Sharing the title row read as unrelated loose parts beside the single dropdown.</summary>
    [Fact]
    public void BreakMusicControls_RenderInABandOfTheirOwn()
    {
        _breakMusic.ActiveProvider.Returns(Substitute.For<IBreakMusicProvider>());

        Load(Performance(), MediaWithArtist("Toto", "Africa"));

        var cut = Render<NowPlayingPanel>();

        Assert.NotEmpty(cut.FindAll(".kh-break-music-bar .kh-break-music-bar__controls button"));
    }

    [Fact]
    public void BreakMusicControls_AreNotInTheHeader()
    {
        _breakMusic.ActiveProvider.Returns(Substitute.For<IBreakMusicProvider>());

        Load(Performance(), MediaWithArtist("Toto", "Africa"));

        var cut = Render<NowPlayingPanel>();

        // The band is a sibling of the header, not inside it: the panel reserves height for one
        // there, and a bar back on the title row would take that room from the song instead.
        Assert.Single(cut.FindAll(".kh-break-music-bar"));
        Assert.Empty(cut.FindAll(".kh-card__header .kh-break-music-bar"));
    }

    [Fact]
    public void ArtistRenders_WhenTheMediaHasOne()
    {
        Load(Performance(), MediaWithArtist("Toto", "Africa"));

        var cut = Render<NowPlayingPanel>();

        Assert.Equal("Toto", cut.Find(ArtistSelector).TextContent);
    }

    [Fact]
    public void ArtistElementIsAbsent_WhenTheMediaHasNoArtist()
    {
        Load(Performance(), MediaWithArtist("", "Africa"));

        var cut = Render<NowPlayingPanel>();

        Assert.Empty(cut.FindAll(ArtistSelector));
    }

    [Fact]
    public void Lanes_TimedLyrics_DrawOneRectPerSungStretchInItsSingersColour()
    {
        var media = Song("duet.kit");
        _lyrics.GetTimedLyricsAsync(media.FilePath, Arg.Any<CancellationToken>()).Returns(Duet());
        Load(Performance(), media);

        var cut = Render<NowPlayingPanel>();

        var spans = cut.FindAll(".kh-now-playing__lanes--by-voice .kh-now-playing__lane-span");
        Assert.Equal(["#0080FF", "#0080FF", "#11A800"], spans.Select(span => span.GetAttribute("fill")));
        Assert.Equal(["25%", "75%", "50%"], spans.Select(span => span.GetAttribute("x")));
        Assert.Contains("kh-now-playing__progress-track--many-lanes", cut.Find(".kh-now-playing__progress-track").ClassName);
        Assert.Single(cut.FindAll(".kh-now-playing__lanes--by-voice .kh-now-playing__played"));
    }

    /// <summary>A lane the song gave no colour must not carry an empty fill attribute, which would
    /// paint it black over the theme's colour.</summary>
    [Fact]
    public void Lanes_ALaneWithNoColour_TakesTheThemeClassAndNoFill()
    {
        var media = Song("plain.kit");
        _lyrics.GetTimedLyricsAsync(media.FilePath, Arg.Any<CancellationToken>())
            .Returns(Lyrics(new LyricPage { ShowFromSeconds = 0, ShowUntilSeconds = 240, Lines = [Line(60, 120)] }));
        Load(Performance(), media);

        var span = Render<NowPlayingPanel>().Find(".kh-now-playing__lanes--by-voice .kh-now-playing__lane-span");

        Assert.Null(span.GetAttribute("fill"));
        Assert.Contains("kh-now-playing__lane-span--themed", span.ClassName);
    }

    [Fact]
    public void Lanes_PlayedWash_CoversTheSongUpToThePlayhead()
    {
        var media = Song("duet.kit");
        _lyrics.GetTimedLyricsAsync(media.FilePath, Arg.Any<CancellationToken>()).Returns(Duet());
        _playback.Position.Returns(TimeSpan.FromSeconds(60));
        Load(Performance(), media);

        var played = Render<NowPlayingPanel>().Find(".kh-now-playing__lanes--by-voice .kh-now-playing__played");

        Assert.Equal("25%", played.GetAttribute("width"));
    }

    /// <summary>A song with no timing keeps today's bar: no lanes, no wash, and the fill still
    /// draws the progress itself.</summary>
    [Fact]
    public void Lanes_NoTimedLyrics_KeepThePlainBar()
    {
        var media = Song("plain.mp4");
        _lyrics.GetTimedLyricsAsync(media.FilePath, Arg.Any<CancellationToken>()).Returns((TimedLyrics?)null);
        Load(Performance(), media);

        var cut = Render<NowPlayingPanel>();

        Assert.Empty(cut.FindAll("svg"));
        Assert.Equal(
            ["kh-now-playing__progress-track", "kh-now-playing__progress-track--seekable"],
            cut.Find(".kh-now-playing__progress-track").ClassList);
        Assert.DoesNotContain("kh-now-playing__progress-fill--over-lanes", cut.Find(".kh-now-playing__progress-fill").ClassName);
    }

    /// <summary>The playhead ticks twice a second for the whole night; reading the song's timing on
    /// each would reopen its container every time.</summary>
    [Fact]
    public void Lanes_PositionTicks_ReadTheLyricsOncePerSong()
    {
        var media = Song("duet.kit");
        _lyrics.GetTimedLyricsAsync(media.FilePath, Arg.Any<CancellationToken>()).Returns(Duet());
        Load(Performance(), media);

        var cut = Render<NowPlayingPanel>();

        for (var tick = 0; tick < 5; tick++)
            _playback.PositionChanged += Raise.Event();
        _broker.Announce(new PlaybackChanged());

        cut.WaitForAssertion(() => _lyrics.Received(1).GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()));
        Assert.Equal(3, cut.FindAll(".kh-now-playing__lanes--by-voice .kh-now-playing__lane-span").Count);
    }

    [Fact]
    public void Lanes_TheNextSong_ReadsItsOwnLyrics()
    {
        var first = Song("duet.kit");
        var second = Song("solo.kit");
        _lyrics.GetTimedLyricsAsync(first.FilePath, Arg.Any<CancellationToken>()).Returns(Duet());
        _lyrics.GetTimedLyricsAsync(second.FilePath, Arg.Any<CancellationToken>())
            .Returns(Lyrics(new LyricPage { ShowFromSeconds = 0, ShowUntilSeconds = 240, Voice = "solo", Lines = [Line(0, 10)] }));
        Load(Performance(), first);

        var cut = Render<NowPlayingPanel>();

        Load(Performance(), second);
        _broker.Announce(new PlaybackChanged());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".kh-now-playing__lanes--by-voice .kh-now-playing__lane-span")));
    }

    /// <summary>A slow read for a song that has already been replaced must not paint its lanes over
    /// the song now playing.</summary>
    [Fact]
    public void Lanes_AReadThatFinishesAfterTheSongChanged_IsDiscarded()
    {
        var first = Song("duet.kit");
        var second = Song("plain.mp4");
        var slow = new TaskCompletionSource<TimedLyrics?>();
        _lyrics.GetTimedLyricsAsync(first.FilePath, Arg.Any<CancellationToken>()).Returns(slow.Task);
        _lyrics.GetTimedLyricsAsync(second.FilePath, Arg.Any<CancellationToken>()).Returns((TimedLyrics?)null);
        Load(Performance(), first);

        var cut = Render<NowPlayingPanel>();

        Load(Performance(), second);
        _broker.Announce(new PlaybackChanged());
        cut.WaitForAssertion(() => _lyrics.Received(1).GetTimedLyricsAsync(second.FilePath, Arg.Any<CancellationToken>()));

        cut.InvokeAsync(() => slow.SetResult(Duet())).Wait();
        // Lets the resumed read run to its end before looking.
        cut.InvokeAsync(() => { }).Wait();

        Assert.Empty(cut.FindAll("svg"));
    }

    private static Media Song(string path) => new()
    {
        FilePath = path,
        Title = "Daddy Cool",
        Duration = TimeSpan.FromSeconds(240),
    };

    /// <summary>Two singers, the first heard twice with a long break between.</summary>
    private static TimedLyrics Duet() => Lyrics(
        new LyricPage { ShowFromSeconds = 0, ShowUntilSeconds = 240, Voice = "♂", Active = new(0x00, 0x80, 0xFF), Lines = [Line(60, 90)] },
        new LyricPage { ShowFromSeconds = 0, ShowUntilSeconds = 240, Voice = "♀", Active = new(0x11, 0xA8, 0x00), Lines = [Line(120, 150)] },
        new LyricPage { ShowFromSeconds = 0, ShowUntilSeconds = 240, Voice = "♂", Active = new(0x00, 0x80, 0xFF), Lines = [Line(180, 200)] });

    private static TimedLyrics Lyrics(params LyricPage[] pages) => new()
    {
        DurationSeconds = 240,
        Bounds = new LyricBox(0, 0, 640, 360),
        Pages = pages,
    };

    private static LyricLine Line(double start, double end) => new() { Syllables = [new(start, end, "la")] };

    private void Load(Performance performance, Media media)
    {
        _playback.CurrentPerformance.Returns(performance);
        _playback.CurrentMedia.Returns(media);
    }

    private static Performance Performance() => new() { SingerId = Guid.NewGuid() };

    private static Media MediaWithArtist(string artist, string title) => new()
    {
        FilePath = "song.mp4",
        Title = title,
        Artist = artist,
    };
}
