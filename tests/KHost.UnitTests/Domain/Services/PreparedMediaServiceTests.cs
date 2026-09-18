using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
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

    /// <summary>Playing a song dequeues it, so the reconcile that follows sees a render nothing
    /// wants. ffmpeg is reading that file at the time: dropping it cuts the song off mid-verse.
    /// </summary>
    [Fact]
    public async Task TheSongAtTheMicrophone_KeepsItsRenderEvenThoughItLeftTheQueue()
    {
        var folder = Directory.CreateTempSubdirectory("khost-keep-playing-");

        try
        {
            var source = Path.Combine(folder.FullName, "song.mp4");
            File.WriteAllText(source, "x");

            var playback = Substitute.For<IPlaybackService>();
            playback.CurrentMedia.Returns(new Media { FilePath = source, Title = "Song" });

            var services = Substitute.For<IServiceProvider>();
            services.GetService(typeof(IPlaybackService)).Returns(playback);

            // Nothing queued at all: the turn was dequeued the moment it started.
            var performances = Substitute.For<IPerformanceService>();
            performances.ReadQueuedAsync().Returns(_ => []);
            services.GetService(typeof(IPerformanceService)).Returns(performances);
            services.GetService(typeof(IMediaService)).Returns(Substitute.For<IMediaService>());

            var service = Service(folder, services);
            var render = RenderFor(service, source, folder);

            await service.ReconcileAsync();

            Assert.True(File.Exists(render), "the render of the song being played was dropped");
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>A song that has just ended is the one most likely to be asked for again, and
    /// re-rendering a kit costs the room a wait. So the first reconcile after it leaves the queue
    /// keeps it: the grace is what makes a replay free.</summary>
    [Fact]
    public async Task ARenderThatJustStoppedBeingWanted_SurvivesTheFirstReconcile()
    {
        var folder = Directory.CreateTempSubdirectory("khost-grace-");

        try
        {
            var source = Path.Combine(folder.FullName, "song.mp4");
            File.WriteAllText(source, "x");

            var service = Service(folder, NothingQueued());
            var render = RenderFor(service, source, folder);

            await service.ReconcileAsync();

            Assert.True(File.Exists(render), "a render was dropped the moment its song left the queue");
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>Re-queued and then dropped again, its grace starts over rather than counting from
    /// the first time it was let go. Otherwise a song queued, sung, and queued again is evicted on
    /// the old clock, which is the case the grace exists for.</summary>
    [Fact]
    public async Task ARenderWantedAgain_StartsItsGraceOver()
    {
        var folder = Directory.CreateTempSubdirectory("khost-regrace-");

        try
        {
            var source = Path.Combine(folder.FullName, "song.mp4");
            File.WriteAllText(source, "x");

            var media = Substitute.For<IMediaService>();
            var mediaId = Guid.NewGuid();
            media.ReadAsync(mediaId).Returns(_ => new Media { Id = mediaId, FilePath = source, Title = "Song" });

            var performances = Substitute.For<IPerformanceService>();
            var services = Substitute.For<IServiceProvider>();
            services.GetService(typeof(IPerformanceService)).Returns(performances);
            services.GetService(typeof(IMediaService)).Returns(media);
            services.GetService(typeof(IPlaybackService)).Returns(Substitute.For<IPlaybackService>());

            var service = Service(folder, services, grace: TimeSpan.FromMilliseconds(120));
            var render = RenderFor(service, source, folder);

            // Let go of once, so a clock starts.
            performances.ReadQueuedAsync().Returns(_ => []);
            await service.ReconcileAsync();

            // Wanted again: that clock must be forgotten.
            performances.ReadQueuedAsync().Returns(_ => [new Performance { MediaId = mediaId, SingerId = Guid.NewGuid() }]);
            await service.ReconcileAsync();

            await Task.Delay(200);

            // Let go of again, just now: the fresh grace keeps it.
            performances.ReadQueuedAsync().Returns(_ => []);
            await service.ReconcileAsync();

            Assert.True(File.Exists(render), "the grace counted from the first time it was let go");
        }
        finally { folder.Delete(recursive: true); }
    }

    private static IServiceProvider NothingQueued()
    {
        var services = Substitute.For<IServiceProvider>();
        var performances = Substitute.For<IPerformanceService>();
        performances.ReadQueuedAsync().Returns(_ => []);
        services.GetService(typeof(IPerformanceService)).Returns(performances);
        services.GetService(typeof(IMediaService)).Returns(Substitute.For<IMediaService>());
        services.GetService(typeof(IPlaybackService)).Returns(Substitute.For<IPlaybackService>());
        return services;
    }

    /// <summary>Past the grace, a render for a song that is neither queued nor playing is dropped:
    /// a long night must not fill the disk with songs nobody is going to ask for again.</summary>
    [Fact]
    public async Task ARenderPastTheGrace_IsDropped()
    {
        var folder = Directory.CreateTempSubdirectory("khost-drop-");

        try
        {
            var source = Path.Combine(folder.FullName, "song.mp4");
            File.WriteAllText(source, "x");

            var services = Substitute.For<IServiceProvider>();
            var performances = Substitute.For<IPerformanceService>();
            performances.ReadQueuedAsync().Returns(_ => []);
            services.GetService(typeof(IPerformanceService)).Returns(performances);
            services.GetService(typeof(IMediaService)).Returns(Substitute.For<IMediaService>());
            services.GetService(typeof(IPlaybackService)).Returns(Substitute.For<IPlaybackService>());

            // No grace, so the drop this is about happens on the first pass.
            var service = Service(folder, services, grace: TimeSpan.Zero);
            var render = RenderFor(service, source, folder);

            await service.ReconcileAsync();

            Assert.False(File.Exists(render), "a render past its grace outlived the reconcile");
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>Stands in for a finished render, named the way the service names one.</summary>
    private static string RenderFor(PreparedMediaService service, string source, DirectoryInfo working)
    {
        var root = Path.Combine(working.FullName, "prepared");
        Directory.CreateDirectory(root);

        // The name is the service's own, read back through the state it reports.
        var render = Directory.GetFiles(root, "*.mp4").FirstOrDefault();
        if (render is null)
        {
            render = Path.Combine(root, NameFor(source));
            File.WriteAllText(render, "rendered");
        }

        return render;
    }

    private static string NameFor(string source)
    {
        var info = new FileInfo(source);
        var seed = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{Path.GetFullPath(source)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(seed))) + ".mp4";
    }

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

    private static PreparedMediaService Service(
        DirectoryInfo? working = null, IServiceProvider? services = null, TimeSpan? grace = null)
        => new(
            NullLogger<PreparedMediaService>.Instance,
            Options.Create(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251/",
                WorkingDirectory = (working ?? Directory.CreateTempSubdirectory("khost-state-root-")).FullName,
            }),
            services ?? Substitute.For<IServiceProvider>(),
            Substitute.For<IMessageBroker>())
        {
            KeepAfterUnwanted = grace ?? TimeSpan.FromMinutes(5),
        };
}
