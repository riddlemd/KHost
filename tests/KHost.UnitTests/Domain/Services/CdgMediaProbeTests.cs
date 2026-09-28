using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using NSubstitute;
using static KHost.UnitTests.Domain.Services.KaraokeZipFixture;

namespace KHost.UnitTests.Domain.Services;

public class CdgMediaProbeTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("khost-cdgprobe").FullName;
    private readonly IMediaProbe _fallback = Substitute.For<IMediaProbe>();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("/songs/a.cdg", true)]
    [InlineData("/songs/a.zip", true)]
    [InlineData("/songs/a.mp3", false)]
    public void CanProbe_ClaimsCompactDiscGraphicsLooseOrZipped(string path, bool expected)
        => Assert.Equal(expected, new CdgMediaProbe(_fallback).CanProbe(path));

    /// <summary>ffprobe on a pipe has tags but no duration, so the inner audio is read off disk.</summary>
    [Fact]
    public async Task ProbeAsync_AZippedPair_DescribesTheAudioInsideAndCleansUp()
    {
        var zip = Write(_directory, "Song.zip", ("song.cdg", Graphics), ("song.mp3", Audio));
        string? probed = null;
        byte[]? probedBytes = null;

        _fallback.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            probed = call.Arg<string>();
            probedBytes = File.ReadAllBytes(probed);
            return new MediaProbeResult { Duration = TimeSpan.FromSeconds(231) };
        });

        var result = await new CdgMediaProbe(_fallback).ProbeAsync(zip);

        Assert.Equal(TimeSpan.FromSeconds(231), result?.Duration);
        Assert.Equal(Audio, probedBytes);
        Assert.Equal(".mp3", Path.GetExtension(probed));
        Assert.False(Directory.Exists(Path.GetDirectoryName(probed)));
    }

    /// <summary>Empty, like a .cdg with nothing beside it: looked, and there is no song.</summary>
    [Fact]
    public async Task ProbeAsync_AZipWithNoPair_IsEmptyAndProbesNothing()
    {
        var zip = Write(_directory, "Song.zip", ("song.cdg", Graphics));

        var result = await new CdgMediaProbe(_fallback).ProbeAsync(zip);

        Assert.NotNull(result);
        Assert.Null(result.Duration);
        await _fallback.DidNotReceiveWithAnyArgs().ProbeAsync(default!);
    }

    /// <summary>Null, not empty: a damaged zip could not be read, which is not the same as no song.</summary>
    [Fact]
    public async Task ProbeAsync_ADamagedZip_CouldNotTell()
    {
        var zip = Path.Combine(_directory, "Song.zip");
        await File.WriteAllBytesAsync(zip, [1, 2, 3, 4]);

        Assert.Null(await new CdgMediaProbe(_fallback).ProbeAsync(zip));
    }
}
