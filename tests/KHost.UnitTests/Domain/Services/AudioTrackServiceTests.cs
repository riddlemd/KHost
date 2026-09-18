using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

/// <summary>What is worth offering a host, given what the probe found. The reading itself belongs to
/// the probe, so these are about the rules rather than about any file format.</summary>
public class AudioTrackServiceTests
{
    private const string Path = "/music/song.mp4";

    private readonly IMediaProbeService _probes = Substitute.For<IMediaProbeService>();

    private AudioTrackService Service() => new(_probes);

    private void Found(params AudioTrack[] tracks)
        => _probes.ProbeAsync(Path, Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { AudioTracks = tracks });

    [Fact]
    public async Task ReadTracks_MusicLeadAndBacking_AreAllOffered()
    {
        Found(
            new AudioTrack(0, AudioTrackRole.Music, "Instrumental"),
            new AudioTrack(1, AudioTrackRole.Backing, "Backing Vocal"),
            new AudioTrack(2, AudioTrackRole.Lead, "Lead Vocal"));

        Assert.Equal(3, (await Service().ReadTracksAsync(Path)).Count);
    }

    /// <summary>One track is nothing to balance, whatever it happens to be called.</summary>
    [Fact]
    public async Task ReadTracks_ASingleTrack_OffersNothing()
    {
        Found(new AudioTrack(0, AudioTrackRole.Music, "Instrumental"));

        Assert.Empty(await Service().ReadTracksAsync(Path));
    }

    /// <summary>Without a music track there is nothing to set the voices against, and mixing what is
    /// left would drop whatever stream the names failed to describe.</summary>
    [Fact]
    public async Task ReadTracks_VoicesWithNoMusic_OffersNothing()
    {
        Found(
            new AudioTrack(0, AudioTrackRole.Lead, "Lead Vocal"),
            new AudioTrack(1, AudioTrackRole.Backing, "Backing Vocal"));

        Assert.Empty(await Service().ReadTracksAsync(Path));
    }

    /// <summary>A file nobody could read is not a file with no tracks: either way there is nothing
    /// to mix, and the song still plays on whatever ffmpeg picks.</summary>
    [Fact]
    public async Task ReadTracks_AFileThatCouldNotBeProbed_OffersNothing()
    {
        _probes.ProbeAsync(Path, Arg.Any<CancellationToken>()).Returns((MediaProbeResult?)null);

        Assert.Empty(await Service().ReadTracksAsync(Path));
    }
}
