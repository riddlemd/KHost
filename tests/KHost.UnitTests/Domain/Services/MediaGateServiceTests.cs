using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public class MediaGateServiceTests
{
    private readonly IMediaTagReader _tags = Substitute.For<IMediaTagReader>();

    private static Media Media() => new() { FilePath = "/media/x.mp4", Title = "x" };

    private static IMediaPlaybackGate Gate(string key, PlaybackGateResult result, bool claims = false)
    {
        var gate = Substitute.For<IMediaPlaybackGate>();
        gate.ProviderId.Returns(key);
        gate.Claims(Arg.Any<string>()).Returns(claims);
        gate.CanAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>()).Returns(result);
        return gate;
    }

    private MediaGateService Service(params IMediaPlaybackGate[] gates)
        => new(NullLogger<MediaGateService>.Instance, _tags, gates);

    [Fact]
    public async Task Evaluate_NoGatesLoaded_Allows()
    {
        var result = await Service().EvaluateAsync(MediaAction.Play, Media());

        Assert.True(result.Allowed);
        // Nothing to gate against, so no reason to read the file.
        await _tags.DidNotReceive().ReadTagAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evaluate_FileHasNoMarker_Allows()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns((string?)null);
        var gate = Gate("KHost.Plugins.Example", new PlaybackGateResult(false, "no"));

        Assert.True((await Service(gate).EvaluateAsync(MediaAction.Play, Media())).Allowed);
        await gate.DidNotReceive().CanAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evaluate_MarkerMatchesNoLoadedGate_Allows()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns("KHost.Plugins.Spotify");

        Assert.True((await Service(Gate("KHost.Plugins.Example", new PlaybackGateResult(false, "no"))).EvaluateAsync(MediaAction.Play, Media())).Allowed);
    }

    [Fact]
    public async Task Evaluate_MarkerMatchesAGate_ReturnsThatGatesDecision()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns("KHost.Plugins.Example");
        var gate = Gate("KHost.Plugins.Example", new PlaybackGateResult(false, "Sign in to the provider."));

        var result = await Service(gate).EvaluateAsync(MediaAction.Play, Media());

        Assert.False(result.Allowed);
        Assert.Equal("Sign in to the provider.", result.Reason);
    }

    /// <summary>A file muxed with one casing must still find a gate that keyed itself in another.</summary>
    [Fact]
    public async Task Evaluate_MarkerMatchesAGate_CaseInsensitively()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns("KHOST.PLUGINS.EXAMPLE");
        var gate = Gate("KHost.Plugins.Example", new PlaybackGateResult(false, "blocked"));

        Assert.False((await Service(gate).EvaluateAsync(MediaAction.Play, Media())).Allowed);
    }

    /// <summary>A provider's bug must not strand the night. The play gate is the one that matters:
    /// an exception escaping it reaches the host with a singer already at the microphone.</summary>
    [Theory]
    [InlineData(MediaAction.Queue)]
    [InlineData(MediaAction.Render)]
    [InlineData(MediaAction.Play)]
    public async Task Evaluate_AGateThatThrowsDeciding_AllowsRatherThanPropagating(MediaAction action)
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns("KHost.Plugins.Example");
        var gate = Substitute.For<IMediaPlaybackGate>();
        gate.ProviderId.Returns("KHost.Plugins.Example");
        gate.CanAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>())
            .Returns<PlaybackGateResult>(_ => throw new InvalidOperationException("the plugin fell over"));

        Assert.True((await Service(gate).EvaluateAsync(action, Media())).Allowed);
    }

    /// <summary>Shutting down is not a provider's bug. Swallowed into an allow, a cancelled load
    /// would report that the song may play at the moment the host is going away.</summary>
    [Fact]
    public async Task Evaluate_AGateCancelled_PropagatesRatherThanAllowing()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns("KHost.Plugins.Example");
        var gate = Substitute.For<IMediaPlaybackGate>();
        gate.ProviderId.Returns("KHost.Plugins.Example");
        gate.CanAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>())
            .Returns<PlaybackGateResult>(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Service(gate).EvaluateAsync(MediaAction.Play, Media()));
    }

    /// <summary>Claims runs for every gate on every untagged file, so a throw there is reached more
    /// often than one in the verdict itself.</summary>
    [Fact]
    public async Task Evaluate_AGateWhoseClaimsThrows_AllowsRatherThanPropagating()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns((string?)null);
        var gate = Substitute.For<IMediaPlaybackGate>();
        gate.ProviderId.Returns("KHost.Plugins.Example");
        gate.Claims(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("the plugin fell over"));

        Assert.True((await Service(gate).EvaluateAsync(MediaAction.Play, Media())).Allowed);
    }

    /// <summary>A tag reader that cannot open the file must not stop the load either.</summary>
    [Fact]
    public async Task Evaluate_ATagReaderThatThrows_AllowsRatherThanPropagating()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new InvalidOperationException("ffprobe fell over"));

        Assert.True((await Service(Gate("KHost.Plugins.Example", new PlaybackGateResult(false, "no"))).EvaluateAsync(MediaAction.Play, Media())).Allowed);
    }

    /// <summary>One plugin instance is bound under several interfaces, so the same gate arrives
    /// more than once; a duplicate key must not throw the whole service at construction.</summary>
    [Fact]
    public async Task Evaluate_TwoGatesSharingAProviderId_DoesNotThrow()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns("KHost.Plugins.Example");
        var first = Gate("KHost.Plugins.Example", new PlaybackGateResult(false, "no"));
        var second = Gate("KHost.Plugins.Example", PlaybackGateResult.Ok);

        Assert.False((await Service(first, second).EvaluateAsync(MediaAction.Play, Media())).Allowed);
    }

    /// <summary>One method carrying the moment as an argument only works while the argument
    /// survives the trip: hardcode it here and every caller silently asks the wrong question.
    /// </summary>
    [Theory]
    [InlineData(MediaAction.Queue)]
    [InlineData(MediaAction.Render)]
    [InlineData(MediaAction.Play)]
    public async Task Evaluate_PassesTheActionToTheGate(MediaAction action)
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns("KHost.Plugins.Example");
        var gate = Gate("KHost.Plugins.Example", PlaybackGateResult.Ok);

        await Service(gate).EvaluateAsync(action, Media());

        await gate.Received(1).CanAsync(action, Arg.Any<Media>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The bypass this exists for: ffprobe cannot open a provider's own container, so the
    /// file reports no tag and an untested fallback would let the whole library through ungated.
    /// </summary>
    [Fact]
    public async Task Evaluate_NoTagButAGateClaimsTheFile_ReturnsThatGatesDecision()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns((string?)null);
        var gate = Gate("KHost.Plugins.Example", new PlaybackGateResult(false, "Sign in to the provider."), claims: true);

        var result = await Service(gate).EvaluateAsync(MediaAction.Play, Media());

        Assert.False(result.Allowed);
        Assert.Equal("Sign in to the provider.", result.Reason);
    }

    /// <summary>A gate that recognises nothing must not answer for somebody else's file.</summary>
    [Fact]
    public async Task Evaluate_NoTagAndNoGateClaimsTheFile_Allows()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns((string?)null);
        var gate = Gate("KHost.Plugins.Example", new PlaybackGateResult(false, "no"), claims: false);

        Assert.True((await Service(gate).EvaluateAsync(MediaAction.Play, Media())).Allowed);
        await gate.DidNotReceive().CanAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A gate that recognises the path owns the file, and the tag is not consulted. The
    /// two disagreeing is pathological; what this is really pinning is that the free question is
    /// asked first, since the tag costs the owning plugin a read of its own container.</summary>
    [Fact]
    public async Task Evaluate_AClaimIsAnswered_WithoutReadingTheTag()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns("KHost.Plugins.Tagged");
        var tagged = Gate("KHost.Plugins.Tagged", PlaybackGateResult.Ok);
        var claiming = Gate("KHost.Plugins.Claiming", new PlaybackGateResult(false, "no"), claims: true);

        var verdict = await Service(tagged, claiming).EvaluateAsync(MediaAction.Play, Media());

        Assert.False(verdict.Allowed);
        await tagged.DidNotReceive().CanAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>());
        await _tags.DidNotReceive().ReadTagAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Nothing claims the name, so the file is asked what it belongs to. This is the
    /// case the tag exists for: a gated render sitting under a name its owner does not claim.
    /// </summary>
    [Fact]
    public async Task Evaluate_NothingClaimsTheName_FallsBackToTheTag()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns("KHost.Plugins.Tagged");
        var tagged = Gate("KHost.Plugins.Tagged", new PlaybackGateResult(false, "no"));

        var verdict = await Service(tagged).EvaluateAsync(MediaAction.Play, Media());

        Assert.False(verdict.Allowed);
        await tagged.Received().CanAsync(MediaAction.Play, Arg.Any<Media>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Claiming is answered from the path alone, so it must not cost a file read.</summary>
    [Fact]
    public async Task Evaluate_AGateClaimsTheFile_AsksTheClaimWithThePath()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns((string?)null);
        var gate = Gate("KHost.Plugins.Example", PlaybackGateResult.Ok, claims: true);

        await Service(gate).EvaluateAsync(MediaAction.Play, Media());

        gate.Received().Claims("/media/x.mp4");
    }
}
