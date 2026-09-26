using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>A renderer answers with stems; the host decides whether the target can take them as they
/// are or needs them encoded into one stream.</summary>
public class MediaRendererServiceTests
{
    private readonly IMediaRenderer _renderer = Substitute.For<IMediaRenderer>();
    private readonly IMediaRenderer _fallback = Substitute.For<IMediaRenderer>();
    private readonly IStemMixdown _mixdown = Substitute.For<IStemMixdown>();

    private static readonly MediaRendition Encoded = new() { Url = "http://host/media/mix/stream.m3u8" };

    private static readonly MediaRendition StemsOnly = new()
    {
        Stems = [new StemSource(0, AudioTrackRole.Music, "http://host/media/s/stem2.ogg", 100)],
        SeekableInPlace = true,
    };

    public MediaRendererServiceTests()
    {
        _renderer.CanRender("/songs/a.song").Returns(true);
        _mixdown.EncodeAsync(default!, default!, default).ReturnsForAnyArgs(Encoded);
    }

    [Fact]
    public async Task RenderAsync_StemsForAMixingDisplayWithNothingChanged_GoStraightThrough()
    {
        _renderer.RenderAsync(default!, default).ReturnsForAnyArgs(StemsOnly);

        var rendition = await Service().RenderAsync(Request(mixes: true));

        Assert.Same(StemsOnly, rendition);
        await _mixdown.DidNotReceiveWithAnyArgs().EncodeAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(false, false, 0, 0)]
    [InlineData(true, true, 0, 0)]
    [InlineData(true, false, 1, 0)]
    [InlineData(true, false, 0, -10)]
    public async Task RenderAsync_StemsATargetCannotTakeAsTheyAre_AreEncodedByTheHost(
        bool mixes, bool burnLyrics, int pitch, int tempo)
    {
        _renderer.RenderAsync(default!, default).ReturnsForAnyArgs(StemsOnly);
        var request = Request(mixes, burnLyrics, pitch, tempo);

        var rendition = await Service().RenderAsync(request);

        Assert.Same(Encoded, rendition);
        await _mixdown.Received(1).EncodeAsync(StemsOnly, request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenderAsync_ARenditionThatBroughtItsOwnStream_IsNeverReencoded()
    {
        var both = new MediaRendition { Url = "http://elsewhere/song.m3u8", Stems = StemsOnly.Stems };
        _renderer.RenderAsync(default!, default).ReturnsForAnyArgs(both);

        var rendition = await Service().RenderAsync(Request(mixes: false, pitch: 2));

        Assert.Same(both, rendition);
        await _mixdown.DidNotReceiveWithAnyArgs().EncodeAsync(default!, default!, default);
    }

    [Fact]
    public async Task RenderAsync_ARendererThatDeclines_LeavesTheSongToTheFallback()
    {
        _renderer.RenderAsync(default!, default).ReturnsForAnyArgs((MediaRendition?)null);
        _fallback.RenderAsync(default!, default).ReturnsForAnyArgs(Encoded);

        Assert.Same(Encoded, await Service().RenderAsync(Request(mixes: false)));
        await _mixdown.DidNotReceiveWithAnyArgs().EncodeAsync(default!, default!, default);
    }

    private MediaRendererService Service()
        => new(NullLogger<MediaRendererService>.Instance, [_renderer], _fallback, _mixdown);

    private static MediaRenderRequest Request(bool mixes, bool burnLyrics = false, int pitch = 0, int tempo = 0) => new()
    {
        FilePath = "/songs/a.song",
        Pitch = pitch,
        Tempo = tempo,
        Target = new RenderTarget { MixesStems = mixes, BurnLyrics = burnLyrics },
    };
}
