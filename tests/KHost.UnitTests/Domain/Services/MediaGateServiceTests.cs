using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class MediaGateServiceTests
{
    private readonly IMediaTagReader _tags = Substitute.For<IMediaTagReader>();

    private static Media Media() => new() { FilePath = "/media/x.mp4", Title = "x" };

    private static IMediaPlaybackGate Gate(string key, PlaybackGateResult result)
    {
        var gate = Substitute.For<IMediaPlaybackGate>();
        gate.GateKey.Returns(key);
        gate.CanPlayAsync(Arg.Any<Media>(), Arg.Any<CancellationToken>()).Returns(result);
        return gate;
    }

    private MediaGateService Service(params IMediaPlaybackGate[] gates) => new(_tags, gates);

    [Fact]
    public async Task Evaluate_NoGatesLoaded_Allows()
    {
        var result = await Service().EvaluateAsync(Media());

        Assert.True(result.Allowed);
        // Nothing to gate against, so no reason to read the file.
        await _tags.DidNotReceive().ReadTagAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evaluate_FileHasNoMarker_Allows()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns((string?)null);
        var gate = Gate("KHost.Plugins.KaraFun", new PlaybackGateResult(false, "no"));

        Assert.True((await Service(gate).EvaluateAsync(Media())).Allowed);
        await gate.DidNotReceive().CanPlayAsync(Arg.Any<Media>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evaluate_MarkerMatchesNoLoadedGate_Allows()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns("KHost.Plugins.Spotify");

        Assert.True((await Service(Gate("KHost.Plugins.KaraFun", new PlaybackGateResult(false, "no"))).EvaluateAsync(Media())).Allowed);
    }

    [Fact]
    public async Task Evaluate_MarkerMatchesAGate_ReturnsThatGatesDecision()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns("KHost.Plugins.KaraFun");
        var gate = Gate("KHost.Plugins.KaraFun", new PlaybackGateResult(false, "Sign in to KaraFun."));

        var result = await Service(gate).EvaluateAsync(Media());

        Assert.False(result.Allowed);
        Assert.Equal("Sign in to KaraFun.", result.Reason);
    }

    /// <summary>A file muxed with one casing must still find a gate that keyed itself in another.</summary>
    [Fact]
    public async Task Evaluate_MarkerMatchesAGate_CaseInsensitively()
    {
        _tags.ReadTagAsync(Arg.Any<string>(), IMediaPlaybackGate.MetadataTag, Arg.Any<CancellationToken>()).Returns("KHOST.PLUGINS.KARAFUN");
        var gate = Gate("KHost.Plugins.KaraFun", new PlaybackGateResult(false, "blocked"));

        Assert.False((await Service(gate).EvaluateAsync(Media())).Allowed);
    }
}
