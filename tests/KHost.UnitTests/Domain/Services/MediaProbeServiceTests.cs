using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Choosing who answers for a file. The fallback reads real files, so what is tested here
/// is the routing: these never reach ffprobe.</summary>
public class MediaProbeServiceTests
{
    private const string Kit = "/library/example/6229.kit";

    // Substituted rather than the real ffprobe: what these test is who gets asked, and a real
    // fallback answers null for every path that does not exist, which hides being called at all.
    private readonly IMediaProbe _fallback = Substitute.For<IMediaProbe>();

    public MediaProbeServiceTests()
        // The real fallback claims every file. Left at the substitute's default of false it is
        // skipped by the loop regardless, which silently stops the ordering test below testing
        // anything at all.
        => _fallback.CanProbe(Arg.Any<string>()).Returns(true);

    private MediaProbeService Service(params IMediaProbe[] probes)
        => new(NullLogger<MediaProbeService>.Instance, probes, _fallback);

    private static IMediaProbe Probe(bool claims, MediaProbeResult? result = null)
    {
        var probe = Substitute.For<IMediaProbe>();
        probe.CanProbe(Arg.Any<string>()).Returns(claims);
        probe.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(result);
        return probe;
    }

    /// <summary>The point of the contract: whoever wrote the container answers for it.</summary>
    [Fact]
    public async Task Probe_AFileAPluginClaims_IsAnsweredByThatPlugin()
    {
        var expected = new MediaProbeResult { Duration = TimeSpan.FromSeconds(243) };

        var result = await Service(Probe(claims: true, expected)).ProbeAsync(Kit);

        Assert.Same(expected, result);
    }

    /// <summary>A plugin that does not recognise the file must not answer for it, or the first
    /// plugin installed would describe every file in the library.</summary>
    [Fact]
    public async Task Probe_AFileNoPluginClaims_FallsBackToFfprobe()
    {
        var expected = new MediaProbeResult { Duration = TimeSpan.FromSeconds(9) };
        _fallback.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(expected);
        var probe = Probe(claims: false);

        Assert.Same(expected, await Service(probe).ProbeAsync("/music/song.mp4"));
        await probe.DidNotReceive().ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The fallback claims every file, so if it were consulted as one of the plugin probes
    /// it would answer for whatever came after it. Registration order is the loader's, not ours, so
    /// this builds the list in the order that exposes it.</summary>
    [Fact]
    public async Task Probe_TheFallbackListedFirst_StillDoesNotAnswerOverTheOwningPlugin()
    {
        var expected = new MediaProbeResult { Duration = TimeSpan.FromSeconds(243) };
        var owner = Probe(claims: true, expected);

        var service = new MediaProbeService(
            NullLogger<MediaProbeService>.Instance, [_fallback, owner], _fallback);


        Assert.Same(expected, await service.ProbeAsync(Kit));
    }

    /// <summary>Its own format, and it could not read it. Falling through to ffprobe would only
    /// produce "Invalid data found", so the null is the answer rather than a reason to keep asking.
    /// </summary>
    [Fact]
    public async Task Probe_TheOwningPluginReturnsNull_DoesNotFallBack()
    {
        // The fallback would happily answer, so this reads whether it was asked rather than only
        // what came back: both paths return null when it has nothing to say.
        _fallback.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Duration = TimeSpan.FromSeconds(9) });

        Assert.Null(await Service(Probe(claims: true, result: null)).ProbeAsync(Kit));
        await _fallback.DidNotReceive().ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A plugin that throws deciding whether a file is its own must not stop the file
    /// being read: ffprobe still has an answer for most things.</summary>
    [Fact]
    public async Task Probe_APluginThatThrowsDeciding_IsSkipped()
    {
        var thrower = Substitute.For<IMediaProbe>();
        thrower.CanProbe(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("fell over"));

        var expected = new MediaProbeResult { Duration = TimeSpan.FromSeconds(1) };

        Assert.Same(expected, await Service(thrower, Probe(claims: true, expected)).ProbeAsync(Kit));
    }

    /// <summary>A plugin that throws reading its own file answers null, the same as one that
    /// returned null: a broken provider must not take the import or the load down with it.</summary>
    [Fact]
    public async Task Probe_APluginThatThrowsReading_AnswersNull()
    {
        var thrower = Substitute.For<IMediaProbe>();
        thrower.CanProbe(Arg.Any<string>()).Returns(true);
        thrower.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<MediaProbeResult?>(_ => throw new InvalidOperationException("fell over"));
        _fallback.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Duration = TimeSpan.FromSeconds(9) });

        Assert.Null(await Service(thrower).ProbeAsync(Kit));
        await _fallback.DidNotReceive().ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>First claim wins, so a second plugin cannot answer over the one that owns it.</summary>
    [Fact]
    public async Task Probe_TwoPluginsClaimTheSameFile_OnlyTheFirstAnswers()
    {
        var first = Probe(claims: true, new MediaProbeResult { Duration = TimeSpan.FromSeconds(1) });
        var second = Probe(claims: true, new MediaProbeResult { Duration = TimeSpan.FromSeconds(2) });

        var result = await Service(first, second).ProbeAsync(Kit);

        Assert.Equal(TimeSpan.FromSeconds(1), result!.Duration);
        await second.DidNotReceive().ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
