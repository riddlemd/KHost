using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class MediaTagReaderTests
{
    private const string Path = "/music/song.mp4";

    private readonly IMediaProbeService _probes = Substitute.For<IMediaProbeService>();

    private MediaTagReader Reader() => new(_probes);

    private void Tagged(params (string Key, string Value)[] tags)
        => _probes.ProbeAsync(Path, Arg.Any<CancellationToken>()).Returns(new MediaProbeResult
        {
            Tags = tags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.OrdinalIgnoreCase),
        });

    [Fact]
    public async Task ReadTag_ATagThatIsThere_ReturnsItsValue()
    {
        Tagged(("khost_provider", "KHost.Plugins.Example"));

        Assert.Equal("KHost.Plugins.Example", await Reader().ReadTagAsync(Path, "khost_provider"));
    }

    /// <summary>Tag names are case-preserving per muxer but effectively case-insensitive across
    /// them, so a reader must not depend on how one file happened to spell one.</summary>
    [Fact]
    public async Task ReadTag_IgnoresTheCaseOfTheTagName()
    {
        Tagged(("KHOST_PROVIDER", "KHost.Plugins.Example"));

        Assert.Equal("KHost.Plugins.Example", await Reader().ReadTagAsync(Path, "khost_provider"));
    }

    [Fact]
    public async Task ReadTag_ATagThatIsNotThere_IsNull()
    {
        Tagged(("title", "Neon Moon"));

        Assert.Null(await Reader().ReadTagAsync(Path, "khost_provider"));
    }

    /// <summary>A file that will not probe is treated as untagged: a gate that cannot read the file
    /// has no business inventing an owner for it.</summary>
    [Fact]
    public async Task ReadTag_AFileThatCouldNotBeProbed_IsNull()
    {
        _probes.ProbeAsync(Path, Arg.Any<CancellationToken>()).Returns((MediaProbeResult?)null);

        Assert.Null(await Reader().ReadTagAsync(Path, "khost_provider"));
    }
}
