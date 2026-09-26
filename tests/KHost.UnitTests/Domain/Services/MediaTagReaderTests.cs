using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class MediaTagReaderTests : IDisposable
{
    // A real file, because reading a tag means opening one and the reader refuses a path with
    // nothing behind it. Its contents never matter: the probe is substituted.
    private readonly string Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"khost-tag-{Guid.NewGuid():N}.mp4");

    private readonly IMediaProbeService _probes = Substitute.For<IMediaProbeService>();

    public MediaTagReaderTests() => File.WriteAllText(Path, "x");

    public void Dispose() => File.Delete(Path);

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

    /// <summary>A file that is not there is untagged without being opened. The gate asks at
    /// enqueue, and a provider's own download is still arriving then, so probing would report a
    /// failure nobody can act on and cost the plugin a full read of nothing.</summary>
    [Fact]
    public async Task ReadTag_AFileThatIsNotThere_IsNullAndIsNeverProbed()
    {
        var missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"khost-gone-{Guid.NewGuid():N}.mp4");

        Assert.Null(await Reader().ReadTagAsync(missing, "khost_provider"));
        await _probes.DidNotReceive().ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
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
