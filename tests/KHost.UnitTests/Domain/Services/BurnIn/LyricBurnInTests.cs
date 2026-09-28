using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.BurnIn;

namespace KHost.UnitTests.Domain.Services.BurnIn;

/// <summary>A song with timed words opens with them burned in; one without is not this class's.</summary>
public class LyricBurnInTests
{
    private readonly ITimedLyricsService _lyrics = Substitute.For<ITimedLyricsService>();
    private readonly IBurnInStreamService _streams = Substitute.For<IBurnInStreamService>();

    private static readonly TimedLyrics Words = new()
    {
        DurationSeconds = 10,
        Bounds = new LyricBox(0, 0, 640, 360),
        Pages = [new LyricPage { ShowFromSeconds = 0, ShowUntilSeconds = 5 }],
    };

    [Fact]
    public async Task OpenAsync_ASongWithWords_OpensThemBurnedIn()
    {
        _lyrics.GetTimedLyricsAsync("/songs/a.mka", Arg.Any<CancellationToken>()).Returns(Words);

        await new LyricBurnIn(_lyrics, _streams).OpenAsync(Request());

        await _streams.Received(1).OpenBurningInAsync(
            "/songs/a.mka", Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(),
            Words, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenAsync_TimingWithNoPages_OpensNothing()
    {
        _lyrics.GetTimedLyricsAsync(default!, default).ReturnsForAnyArgs(Words with { Pages = [] });

        var session = await new LyricBurnIn(_lyrics, _streams).OpenAsync(Request());

        Assert.Null(session);
        await _streams.DidNotReceiveWithAnyArgs().OpenBurningInAsync(default!, default, default, default, default, default!);
    }

    private static MediaRenderRequest Request() => new()
    {
        FilePath = "/songs/a.mka",
        Target = new RenderTarget { BurnLyrics = true },
    };
}
