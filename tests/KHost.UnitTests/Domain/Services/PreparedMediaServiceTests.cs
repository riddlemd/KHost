using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Rendering a queued song ahead of play time, and the conditions under which the render
/// may be copied rather than encoded again.</summary>
public class PreparedMediaServiceTests
{
    /// <summary>The only state in which every filter the transcode builds is empty. A wrong true
    /// here is silent: the song plays, in the wrong key, with nothing to say why.</summary>
    [Fact]
    public void NothingFiltered_MayBeCopied()
        => Assert.True(HlsMediaStreamService.CanStreamCopy(pitch: 0, tempo: 0, mix: null));

    [Theory]
    [InlineData(1)]
    [InlineData(-3)]
    public void AShiftedKey_MustBeEncodedAgain(int pitch)
        => Assert.False(HlsMediaStreamService.CanStreamCopy(pitch, tempo: 0, mix: null));

    /// <summary>Tempo rewrites the video as well as the audio, so neither half survives a copy.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    public void AChangedTempo_MustBeEncodedAgain(int tempo)
        => Assert.False(HlsMediaStreamService.CanStreamCopy(pitch: 0, tempo, mix: null));

    /// <summary>A mix re-levels the tracks against each other, which is an encode by definition.
    /// </summary>
    [Fact]
    public void ARelevelledMix_MustBeEncodedAgain()
    {
        var mix = new AudioMix(
            [new AudioTrack(0, AudioTrackRole.Music, "music"), new AudioTrack(1, AudioTrackRole.Lead, "lead")],
            LeadVolume: 20,
            BackingVolume: 100);

        Assert.True(mix.IsMixable);
        Assert.False(HlsMediaStreamService.CanStreamCopy(pitch: 0, tempo: 0, mix: mix));
    }

    /// <summary>A file with one track has nothing to balance, so its "mix" filters nothing and the
    /// copy still stands.</summary>
    [Fact]
    public void AMixWithNothingToBalance_StillAllowsACopy()
    {
        var mix = new AudioMix([new AudioTrack(0, AudioTrackRole.Music, "music")], LeadVolume: 0, BackingVolume: 100);

        Assert.False(mix.IsMixable);
        Assert.True(HlsMediaStreamService.CanStreamCopy(pitch: 0, tempo: 0, mix: mix));
    }

    /// <summary>A file the host can transcode is playable whether or not a render exists, so an
    /// unprepared turn is not a turn that is waiting: it starts the moment it is asked.</summary>
    [Fact]
    public void AFileWithNoRender_IsUnprepared()
    {
        var folder = Directory.CreateTempSubdirectory("khost-state-tests-");

        try
        {
            var source = Path.Combine(folder.FullName, "song.mp4");
            File.WriteAllText(source, "x");

            Assert.Equal(PerformancePreparation.Unprepared, Service(folder).StateFor(source));
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>A path that resolves to nothing is nobody's turn to wait on.</summary>
    [Fact]
    public void AMissingFile_IsUnprepared()
        => Assert.Equal(PerformancePreparation.Unprepared, Service().StateFor("/nowhere/song.mp4"));

    [Fact]
    public void AnEmptyPath_IsUnprepared()
        => Assert.Equal(PerformancePreparation.Unprepared, Service().StateFor(""));

    /// <summary>The segmenter cuts on keyframes, so a render without the same cadence cannot be
    /// copied into segments where HLS needs them.</summary>
    [Fact]
    public void TheRender_ForcesTheSegmentersKeyframeCadence()
    {
        var arguments = PreparedMediaService.BuildArguments("/music/song.mp4", "/tmp/out.mp4", segmentSeconds: 4);

        Assert.Contains("-force_key_frames \"expr:gte(t,n_forced*4)\"", arguments, StringComparison.Ordinal);
        Assert.Contains("-sc_threshold 0", arguments, StringComparison.Ordinal);
    }

    /// <summary>What the copy path will later stream: H.264 and AAC, or the copy has nothing it can
    /// hand to a segmenter without encoding it.</summary>
    [Fact]
    public void TheRender_ProducesWhatTheCopyPathCanStream()
    {
        var arguments = PreparedMediaService.BuildArguments("/music/song.mp4", "/tmp/out.mp4", segmentSeconds: 4);

        Assert.Contains("-c:v libx264", arguments, StringComparison.Ordinal);
        Assert.Contains("-c:a aac", arguments, StringComparison.Ordinal);
    }

    /// <summary>A render is written to a .part name so a half-finished one is never resolved, and
    /// ffmpeg reads the format off the extension: unnamed, it refuses the file outright.</summary>
    [Fact]
    public void TheRender_NamesTheMuxerRatherThanLeavingItToTheExtension()
    {
        var arguments = PreparedMediaService.BuildArguments("/music/song.mp4", "/tmp/out.mp4.part", segmentSeconds: 4);

        Assert.Contains("-f mp4", arguments, StringComparison.Ordinal);
    }

    /// <summary>A .cdg holds only graphics, so a render that ignored the .mp3 beside it would be a
    /// silent song that plays perfectly.</summary>
    [Fact]
    public void ACdg_RendersWithTheAudioBesideIt()
    {
        var folder = Directory.CreateTempSubdirectory("khost-prepared-tests-");

        try
        {
            var cdg = Path.Combine(folder.FullName, "song.cdg");
            File.WriteAllText(cdg, "graphics");
            File.WriteAllText(Path.Combine(folder.FullName, "song.mp3"), "audio");

            var arguments = PreparedMediaService.BuildArguments(cdg, "/tmp/out.mp4", segmentSeconds: 4);

            Assert.Contains("song.mp3", arguments, StringComparison.Ordinal);
            Assert.Contains("-map 0:v:0 -map 1:a:0", arguments, StringComparison.Ordinal);

            // Without a constant rate its forced keyframes land on sparse frames instead of on the
            // boundaries, and the copy then cuts segments of zero and nine seconds.
            Assert.Contains("-r 30", arguments, StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>An ordinary video carries its own sound, and mapping a second input it does not have
    /// would fail the render outright.</summary>
    [Fact]
    public void AVideo_RendersWithoutLookingForACompanion()
    {
        var arguments = PreparedMediaService.BuildArguments("/music/song.mp4", "/tmp/out.mp4", segmentSeconds: 4);

        Assert.DoesNotContain("-map 1:a:0", arguments, StringComparison.Ordinal);

        // Never forced on real video: it already has a frame rate, and imposing one resamples it.
        Assert.DoesNotContain("-r 30", arguments, StringComparison.Ordinal);
    }

    /// <summary>The copy re-segments and nothing else. Any encoder here would be the cost this
    /// whole path exists to avoid.</summary>
    [Fact]
    public void TheCopy_ReSegmentsWithoutTouchingTheFrames()
    {
        var arguments = HlsMediaStreamService.BuildCopyArguments("/tmp/prepared.mp4", TimeSpan.Zero, segmentSeconds: 4);

        Assert.Contains("-c copy", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("libx264", arguments, StringComparison.Ordinal);
        Assert.Contains("-f hls", arguments, StringComparison.Ordinal);
    }

    /// <summary>Resuming mid-song has to seek the render, not start it over.</summary>
    [Fact]
    public void TheCopy_SeeksWhenStartingPartWayIn()
    {
        var arguments = HlsMediaStreamService.BuildCopyArguments("/tmp/prepared.mp4", TimeSpan.FromSeconds(42), segmentSeconds: 4);

        Assert.Contains("-ss 42.000", arguments, StringComparison.Ordinal);
    }

    private static PreparedMediaService Service(DirectoryInfo? working = null)
        => new(
            NullLogger<PreparedMediaService>.Instance,
            Options.Create(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251/",
                WorkingDirectory = (working ?? Directory.CreateTempSubdirectory("khost-state-root-")).FullName,
            }),
            Substitute.For<IServiceProvider>(),
            Substitute.For<IMessageBroker>());
}
