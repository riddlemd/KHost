using KHost.Abstractions.Services;
using KHost.Common.Media;
using KHost.Domain;
using KHost.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using static KHost.UnitTests.Domain.Services.KaraokeZipFixture;

namespace KHost.UnitTests.Domain.Services;

public class ZippedKaraokeSourceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("khost-zipsource").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("/songs/a.zip", true)]
    [InlineData("/songs/A.ZIP", true)]
    [InlineData("/songs/a.cdg", false)]
    [InlineData("/songs/a.mp4", false)]
    public void CanResolve_ClaimsAZipAndNothingElse(string path, bool expected)
        => Assert.Equal(expected, new ZippedKaraokeSource().CanResolve(path));

    [Fact]
    public async Task ResolvePlayableAsync_HandsBackTheWrittenGraphics_WithItsAudioBesideIt()
    {
        var zip = Write(_directory, "Song.zip", ("Song.CDG", Graphics), ("song.mp3", Audio));
        var session = Directory.CreateDirectory(Path.Combine(_directory, "session")).FullName;

        var resolved = await new ZippedKaraokeSource().ResolvePlayableAsync(zip, session);

        Assert.Equal(Path.Combine(session, "karaoke.cdg"), resolved);
        Assert.Equal(Path.Combine(session, "karaoke.mp3"), MediaFormats.FindKaraokeAudio(resolved!));
    }

    /// <summary>The host's source registers ahead of every plugin's, so a zip is never a plugin's to take.</summary>
    [Fact]
    public void AddDomain_RegistersTheZipSourceAsAPlayableSource()
    {
        var services = new ServiceCollection().AddDomain();

        Assert.Contains(services, d => d.ServiceType == typeof(IPlayableMediaSource)
                                       && d.ImplementationType == typeof(ZippedKaraokeSource));
    }
}
