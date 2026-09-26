using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Backgrounds;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.BurnIn;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>The host's encode from a renderer's stems: cut at the playhead with the key and tempo
/// asked for, and the words burned in only for a display that asked for them.</summary>
public class StemMixdownTests
{
    private readonly IStemStreamService _stemStreams = Substitute.For<IStemStreamService>();
    private readonly IMediaStreamService _streams = Substitute.For<IMediaStreamService>();
    private readonly IBurnInStreamService _burnInStreams = Substitute.For<IBurnInStreamService>();
    private readonly ITimedLyricsService _lyrics = Substitute.For<ITimedLyricsService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IBackgroundPackService _backgrounds = Substitute.For<IBackgroundPackService>();

    private static readonly TimedLyrics Words = new()
    {
        DurationSeconds = 10,
        Bounds = new LyricBox(0, 0, 640, 360),
        Pages = [new LyricPage { ShowFromSeconds = 0, ShowUntilSeconds = 5 }],
    };

    private static readonly MediaStreamSession StemSession = new()
    {
        Id = "stems", SourcePath = "/songs/a.song", StartOffset = TimeSpan.Zero, Pitch = 0, Tempo = 0,
    };

    private static readonly MediaRendition Stems = new()
    {
        Stems =
        [
            new StemSource(0, AudioTrackRole.Music, "http://host/media/stems/stem2.ogg", 100),
            new StemSource(1, AudioTrackRole.Lead, "http://host/media/stems/stem4.ogg", 0) { Voice = "Ann" },
        ],
        SeekableInPlace = true,
        Session = StemSession,
    };

    public StemMixdownTests()
    {
        _stemStreams.OpenStemsAsync(default!, default!, default, default, default, default, default, default)
            .ReturnsForAnyArgs(new MediaStreamSession
            {
                Id = "mix",
                SourcePath = "/songs/a.song",
                PlaylistUrl = "http://host/media/mix/stream.m3u8",
                StartOffset = TimeSpan.FromSeconds(30),
                Pitch = 2,
                Tempo = -5,
            });
        _venues.ReadSelectedVenueAsync().Returns((Venue?)null);
        _lyrics.GetTimedLyricsAsync("/songs/a.song", Arg.Any<CancellationToken>()).Returns(Words);
    }

    [Fact]
    public async Task EncodeAsync_OpensOneStreamFromTheStems_AdoptingTheirSession()
    {
        var rendition = await Mixdown().EncodeAsync(Stems, Request(burnLyrics: false));

        await _stemStreams.Received(1).OpenStemsAsync(
            "/songs/a.song", Stems.Stems, TimeSpan.FromSeconds(30), 2, -5, null, null, StemSession,
            Arg.Any<CancellationToken>());
        Assert.Equal("http://host/media/mix/stream.m3u8", rendition.Url);
        Assert.Empty(rendition.Stems);
        Assert.Equal(TimeSpan.FromSeconds(30), rendition.StartOffset);
        Assert.Equal((2, -5), (rendition.Pitch, rendition.Tempo));
        Assert.False(rendition.SeekableInPlace);
        Assert.Equal("mix", rendition.Session!.Id);
    }

    [Fact]
    public async Task EncodeAsync_ADisplayAskingForWords_GetsTheSongsWordsBurnedIn()
    {
        await Mixdown().EncodeAsync(Stems, Request(burnLyrics: true));

        await _stemStreams.Received(1).OpenStemsAsync(
            "/songs/a.song", Stems.Stems, TimeSpan.FromSeconds(30), 2, -5, Words, null, StemSession,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EncodeAsync_ADisplayDrawingItsOwnWords_NeverHasThemRead()
    {
        await Mixdown().EncodeAsync(Stems, Request(burnLyrics: false));

        await _lyrics.DidNotReceiveWithAnyArgs().GetTimedLyricsAsync(default!, default);
    }

    private StemMixdown Mixdown() => new(
        _stemStreams,
        _streams,
        new LyricBurnIn(_lyrics, _venues, _backgrounds, _burnInStreams, NullLogger<LyricBurnIn>.Instance));

    private static MediaRenderRequest Request(bool burnLyrics) => new()
    {
        FilePath = "/songs/a.song",
        StartOffset = TimeSpan.FromSeconds(30),
        Pitch = 2,
        Tempo = -5,
        Target = new RenderTarget { MixesStems = false, BurnLyrics = burnLyrics },
    };
}
