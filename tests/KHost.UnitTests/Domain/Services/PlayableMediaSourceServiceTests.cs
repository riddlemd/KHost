using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Choosing who turns a file into something the host can open, and what happens when
/// nobody can.</summary>
public class PlayableMediaSourceServiceTests
{
    private const string Kit = "/library/karafun/6229.kit";
    private const string Session = "/tmp/khost-streams/abc";
    private const string Converted = "/tmp/khost-streams/abc/6229.kfa";

    private static IPlayableMediaSource Source(bool claims, string? answer = null)
    {
        var source = Substitute.For<IPlayableMediaSource>();
        source.CanResolve(Arg.Any<string>()).Returns(claims);
        source.ResolvePlayableAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(answer);
        return source;
    }

    private static PlayableMediaSourceService Service(params IPlayableMediaSource[] sources)
        => new(NullLogger<PlayableMediaSourceService>.Instance, sources);

    [Fact]
    public async Task ResolvePlayableAsync_NobodyClaimsTheFile_AnswersThePathItWasGiven()
    {
        var resolved = await Service(Source(claims: false)).ResolvePlayableAsync("/library/song.mp4", Session);

        // The overwhelmingly common case: nothing claims an mp4, and the host opens it directly.
        Assert.Equal("/library/song.mp4", resolved);
    }

    [Fact]
    public async Task ResolvePlayableAsync_TheOwnerConverts_AnswersTheConvertedPath()
    {
        var mine = Source(claims: true, answer: Converted);

        Assert.Equal(Converted, await Service(Source(claims: false), mine).ResolvePlayableAsync(Kit, Session));
        await mine.Received(1).ResolvePlayableAsync(Kit, Session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolvePlayableAsync_Always_HandsOverTheSessionDirectoryToWriteIn()
    {
        var mine = Source(claims: true, answer: Converted);

        await Service(mine).ResolvePlayableAsync(Kit, Session);

        // The converted copy has to land where the session's own cleanup will sweep it; a source
        // writing beside the library would leave a playable copy of every song it ever opened.
        await mine.Received(1).ResolvePlayableAsync(Arg.Any<string>(), Session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolvePlayableAsync_ASourceThrowsDeciding_SkipsItAndAsksTheNext()
    {
        var broken = Substitute.For<IPlayableMediaSource>();
        broken.CanResolve(Arg.Any<string>()).Throws(new InvalidOperationException("boom"));

        // One plugin having a bad day must not stop a song another plugin can open.
        Assert.Equal(Converted, await Service(broken, Source(claims: true, answer: Converted))
            .ResolvePlayableAsync(Kit, Session));
    }

    [Fact]
    public async Task ResolvePlayableAsync_TheOwnerCannotConvert_LetsTheFailureOut()
    {
        var broken = Substitute.For<IPlayableMediaSource>();
        broken.CanResolve(Arg.Any<string>()).Returns(true);
        broken.ResolvePlayableAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidDataException("not a kit"));

        // Swallowing it would hand ffmpeg the original, which it cannot read either — the room
        // gets a song that never starts and the log never names why.
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(broken).ResolvePlayableAsync(Kit, Session));
    }

    [Fact]
    public async Task ResolvePlayableAsync_ASourceClaimsButHasNothingToDo_FallsBackToTheOriginal()
    {
        // Null is "nothing to do here", not a failure.
        Assert.Equal(Kit, await Service(Source(claims: true, answer: null)).ResolvePlayableAsync(Kit, Session));
    }
}
