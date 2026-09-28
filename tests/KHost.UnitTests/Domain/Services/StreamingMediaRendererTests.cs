using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.BurnIn;

namespace KHost.UnitTests.Domain.Services;

/// <summary>A display that cannot draw words asks for them burned in, and any song with timed words
/// gets them; every other song, and every other display, gets the ordinary encode.</summary>
public class StreamingMediaRendererTests
{
    private readonly IMediaStreamService _streams = Substitute.For<IMediaStreamService>();
    private readonly IBurnInStreamService _burnInStreams = Substitute.For<IBurnInStreamService>();
    private readonly ITimedLyricsService _lyrics = Substitute.For<ITimedLyricsService>();

    private static readonly TimedLyrics Words = new()
    {
        DurationSeconds = 10,
        Bounds = new LyricBox(0, 0, 640, 360),
        Pages = [new LyricPage { ShowFromSeconds = 0, ShowUntilSeconds = 5 }],
    };

    public StreamingMediaRendererTests()
    {
        _streams.OpenAsync(default!, default, default, default, default, default)
            .ReturnsForAnyArgs(Session("plain"));
        _burnInStreams.OpenBurningInAsync(default!, default, default, default, default, default!, default)
            .ReturnsForAnyArgs(Session("burned"));
    }

    [Fact]
    public async Task RenderAsync_ADisplayAskingForWords_GetsThemBurnedIn_WhenTheSongHasThem()
    {
        _lyrics.GetTimedLyricsAsync("/songs/a.mka", Arg.Any<CancellationToken>()).Returns(Words);

        var rendition = await Renderer().RenderAsync(Request(burnLyrics: true));

        Assert.Equal("http://host/media/burned/stream.m3u8", rendition!.Url);
        await _burnInStreams.Received(1).OpenBurningInAsync(
            "/songs/a.mka", TimeSpan.FromSeconds(12), 2, -5, null, Words, Arg.Any<CancellationToken>());
        await _streams.DidNotReceiveWithAnyArgs().OpenAsync(default!);
    }

    [Fact]
    public async Task RenderAsync_ADisplayAskingForWords_GetsThePlainEncode_WhenTheSongHasNone()
    {
        _lyrics.GetTimedLyricsAsync(default!, default).ReturnsForAnyArgs((TimedLyrics?)null);

        var rendition = await Renderer().RenderAsync(Request(burnLyrics: true));

        Assert.Equal("http://host/media/plain/stream.m3u8", rendition!.Url);
        await _burnInStreams.DidNotReceiveWithAnyArgs().OpenBurningInAsync(default!, default, default, default, default, default!);
    }

    [Fact]
    public async Task RenderAsync_ADisplayAskingForWords_GetsThePlainEncode_WhenTheTimingHasNoPages()
    {
        _lyrics.GetTimedLyricsAsync(default!, default).ReturnsForAnyArgs(Words with { Pages = [] });

        var rendition = await Renderer().RenderAsync(Request(burnLyrics: true));

        Assert.Equal("http://host/media/plain/stream.m3u8", rendition!.Url);
    }

    /// <summary>A display that draws its own words is not sent a picture with them in it, and the
    /// lyrics are not even read for it.</summary>
    [Fact]
    public async Task RenderAsync_ADisplayNotAskingForWords_GetsThePlainEncode()
    {
        _lyrics.GetTimedLyricsAsync(default!, default).ReturnsForAnyArgs(Words);

        var rendition = await Renderer().RenderAsync(Request(burnLyrics: false));

        Assert.Equal("http://host/media/plain/stream.m3u8", rendition!.Url);
        await _lyrics.DidNotReceiveWithAnyArgs().GetTimedLyricsAsync(default!);
    }


    /// <summary>A format whose words are already in its picture passes no burn-in, so a display
    /// asking for words gets that picture as it is.</summary>
    [Fact]
    public async Task RenderAsync_WithNoBurnIn_GetsThePlainEncodeWhateverIsAsked()
    {
        _lyrics.GetTimedLyricsAsync(default!, default).ReturnsForAnyArgs(Words);

        var rendition = await new StreamingMediaRenderer(_streams).RenderAsync(Request(burnLyrics: true));

        Assert.Equal("http://host/media/plain/stream.m3u8", rendition!.Url);
    }

    private StreamingMediaRenderer Renderer() => new(
        _streams,
        new LyricBurnIn(_lyrics, _burnInStreams));

    private static MediaRenderRequest Request(bool burnLyrics) => new()
    {
        FilePath = "/songs/a.mka",
        StartOffset = TimeSpan.FromSeconds(12),
        Pitch = 2,
        Tempo = -5,
        Target = new RenderTarget { BurnLyrics = burnLyrics },
    };

    private static MediaStreamSession Session(string id) => new()
    {
        Id = id,
        SourcePath = "/songs/a.mka",
        PlaylistUrl = $"http://host/media/{id}/stream.m3u8",
        StartOffset = TimeSpan.FromSeconds(12),
        Pitch = 2,
        Tempo = -5,
    };
}
