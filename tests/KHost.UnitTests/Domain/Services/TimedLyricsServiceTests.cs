using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Choosing who answers for a file, and what happens when nobody can.</summary>
public class TimedLyricsServiceTests
{
    private const string Kit = "/library/karafun/6229.kfa";

    private static TimedLyrics SomeLyrics() => new()
    {
        DurationSeconds = 120,
        Bounds = new LyricBox(0, 0, 640, 360),
    };

    private static ITimedLyricsProvider Provider(bool claims, TimedLyrics? answer = null)
    {
        var provider = Substitute.For<ITimedLyricsProvider>();
        provider.CanProvide(Arg.Any<string>()).Returns(claims);
        provider.GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(answer);
        return provider;
    }

    private static TimedLyricsService Service(params ITimedLyricsProvider[] providers)
        => new(NullLogger<TimedLyricsService>.Instance, providers);

    [Fact]
    public async Task GetTimedLyricsAsync_AsksTheProviderThatClaimsTheFile()
    {
        var mine = Provider(claims: true, answer: SomeLyrics());
        var other = Provider(claims: false);

        var lyrics = await Service(other, mine).GetTimedLyricsAsync(Kit);

        Assert.NotNull(lyrics);
        await mine.Received(1).GetTimedLyricsAsync(Kit, Arg.Any<CancellationToken>());
        await other.DidNotReceive().GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTimedLyricsAsync_AnswersNull_WhenNobodyClaimsTheFile()
    {
        var lyrics = await Service(Provider(claims: false), Provider(claims: false))
            .GetTimedLyricsAsync("/library/song.mp4");

        // The ordinary case, not a failure: almost nothing carries timing, and those songs play
        // with whatever picture the host already streams.
        Assert.Null(lyrics);
    }

    [Fact]
    public async Task GetTimedLyricsAsync_SkipsAProviderThatThrowsDeciding()
    {
        var broken = Substitute.For<ITimedLyricsProvider>();
        broken.CanProvide(Arg.Any<string>()).Throws(new InvalidOperationException("boom"));
        var mine = Provider(claims: true, answer: SomeLyrics());

        var lyrics = await Service(broken, mine).GetTimedLyricsAsync(Kit);

        // One plugin having a bad day must not cost the song its words.
        Assert.NotNull(lyrics);
    }

    [Fact]
    public async Task GetTimedLyricsAsync_AnswersNull_WhenTheOwnerCannotReadItsOwnFile()
    {
        var broken = Substitute.For<ITimedLyricsProvider>();
        broken.CanProvide(Arg.Any<string>()).Returns(true);
        broken.GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidDataException("not a kit"));
        var later = Provider(claims: true, answer: SomeLyrics());

        var lyrics = await Service(broken, later).GetTimedLyricsAsync(Kit);

        // Nobody else can read a container its owner could not, so the search stops rather than
        // handing the file to a provider that would answer about a format it never wrote.
        Assert.Null(lyrics);
        await later.DidNotReceive().GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTimedLyricsAsync_LetsCancellationThrough()
    {
        var provider = Substitute.For<ITimedLyricsProvider>();
        provider.CanProvide(Arg.Any<string>()).Returns(true);
        provider.GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        // A cancelled load is not a plugin that failed, and swallowing it would report "no words"
        // for a song that was simply abandoned.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Service(provider).GetTimedLyricsAsync(Kit));
    }
}
