using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
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

    /// <summary>Tempo retimes the frames, so it is the one thing that rules the picture out.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    public void AChangedTempo_RulesOutCopyingThePicture(int tempo)
        => Assert.False(HlsMediaStreamService.CanCopyVideo(tempo));

    [Fact]
    public void NothingRetimed_MayCopyThePicture()
        => Assert.True(HlsMediaStreamService.CanCopyVideo(tempo: 0));

    /// <summary>The split exists for these two. Both rebuild the audio and neither touches a
    /// frame, so re-encoding the picture for either is work with no output to show for it.
    /// </summary>
    [Fact]
    public void AShiftedKey_LeavesThePictureCopyable()
    {
        Assert.False(HlsMediaStreamService.CanCopyAudio(pitch: 2, tempo: 0, mix: null));
        Assert.True(HlsMediaStreamService.CanCopyVideo(tempo: 0));
    }

    [Fact]
    public void ARelevelledMix_LeavesThePictureCopyable()
    {
        var mix = new AudioMix(
            [new AudioTrack(0, AudioTrackRole.Music, "music"), new AudioTrack(1, AudioTrackRole.Lead, "lead")],
            LeadVolume: 20,
            BackingVolume: 100);

        Assert.False(HlsMediaStreamService.CanCopyAudio(pitch: 0, tempo: 0, mix: mix));
        Assert.True(HlsMediaStreamService.CanCopyVideo(tempo: 0));
    }

    /// <summary>A no from either half is a no to the whole-file copy.</summary>
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(2, 0, false)]
    [InlineData(0, 10, false)]
    public void TheWholeCopy_NeedsBothHalves(int pitch, int tempo, bool expected)
        => Assert.Equal(expected, HlsMediaStreamService.CanStreamCopy(pitch, tempo, mix: null));

    /// <summary>Nothing is copied from the original file, whatever the filters say. It carries no
    /// promise about where its keyframes are, and the muxer can only cut on one.</summary>
    [Fact]
    public void WithNoRender_NothingIsCopied()
        => Assert.Equal((false, false), HlsMediaStreamService.CopyPlan(hasPrepared: false, 0, 0, mix: null));

    [Fact]
    public void WithARenderAndNoFilters_TheWholeFileIsCopied()
        => Assert.Equal((true, false), HlsMediaStreamService.CopyPlan(hasPrepared: true, 0, 0, mix: null));

    /// <summary>The whole point of the split: the audio is rebuilt and the picture is not.</summary>
    [Fact]
    public void WithARenderAndAShiftedKey_OnlyThePictureIsCopied()
        => Assert.Equal((false, true), HlsMediaStreamService.CopyPlan(hasPrepared: true, pitch: 2, tempo: 0, mix: null));

    [Fact]
    public void WithARenderAndARelevelledMix_OnlyThePictureIsCopied()
    {
        var mix = new AudioMix(
            [new AudioTrack(0, AudioTrackRole.Music, "music"), new AudioTrack(1, AudioTrackRole.Lead, "lead")],
            LeadVolume: 20,
            BackingVolume: 100);

        Assert.Equal((false, true), HlsMediaStreamService.CopyPlan(hasPrepared: true, 0, 0, mix));
    }

    /// <summary>Tempo retimes the frames, so it is the one filter that leaves nothing to carry.
    /// </summary>
    [Fact]
    public void WithARenderAndAChangedTempo_NothingIsCopied()
        => Assert.Equal((false, false), HlsMediaStreamService.CopyPlan(hasPrepared: true, 0, tempo: 10, mix: null));

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

            // No grace, or the render survives on that alone and this proves nothing about the
            // song at the microphone being kept.
            var service = Service(folder, services, grace: TimeSpan.Zero);
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

    /// <summary>An ordinary file is playable the whole time it is being pre-rendered: the transcode
    /// path has always been there and the render is only a shortcut. This is the case greying on
    /// "preparing" alone would get wrong, taking away a song that starts instantly.</summary>
    [Fact]
    public void AFileTheHostCanReadItself_IsNeverWaitingOnARender()
    {
        var folder = Directory.CreateTempSubdirectory("khost-waiting-ordinary-");

        try
        {
            var source = Path.Combine(folder.FullName, "song.mp4");
            File.WriteAllText(source, "x");

            Assert.False(Service(folder).IsWaitingOnARender(source));
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>The other half the enum gets wrong: a plugin's format that has not begun rendering
    /// is unprepared rather than preparing, and cannot start at all.</summary>
    [Fact]
    public async Task APluginsFormatWithNoRender_IsWaitingOnARender()
    {
        await using var queued = new QueuedKit();

        Assert.True(queued.Service.IsWaitingOnARender(queued.Source));
    }

    /// <summary>Once the render is there the turn starts off it, so nothing is being waited on.
    /// </summary>
    [Fact]
    public async Task APluginsFormatOnceRendered_IsNotWaitingOnARender()
    {
        await using var queued = new QueuedKit();

        queued.LandTheRender();

        Assert.False(queued.Service.IsWaitingOnARender(queued.Source));
    }

    /// <summary>One unrenderable file must not take the single render slot on every queue change.
    /// Retried forever it starves every other song in the queue, which is worse than the one song
    /// nobody can play.</summary>
    [Fact]
    public async Task AFileThatFailedToRender_IsNotRetriedOnTheNextReconcile()
    {
        // The fixture's preparer claims the file and cannot produce one, which is the case here.
        await using var queued = new QueuedKit();

        await queued.Service.ReconcileAsync();
        await queued.Service.ReconcileAsync();
        await queued.Service.ReconcileAsync();

        await queued.Preparer.Received(1).PrepareAsync(
            queued.Source, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Two passes can both read the failure memo before either writes one, and the
    /// in-flight entry that would otherwise join them is removed the moment a render ends. So a
    /// pass that looked early and arrived late makes its own entry and renders the same file
    /// twice, which is the exact thing the in-flight entry exists to stop.</summary>
    /// <remarks>The interleaving is forced rather than raced for: the gate is the first await after
    /// the memo is read, so holding a second pass there and letting the first finish puts it
    /// exactly in the window. Found by running the suite on a fully loaded machine, where it failed
    /// two runs in three.</remarks>
    [Fact]
    public async Task TwoPassesEitherSideOfAFailedRender_StillRenderOnlyOnce()
    {
        // Nothing queued yet, so the constructor's own pass has no work and cannot reach the
        // substitutes this test is about to arrange.
        await using var queued = new QueuedKit(queued: false);
        await queued.ReadyAsync();

        var secondAtTheGate = new TaskCompletionSource();
        var releaseSecond = new TaskCompletionSource();
        var renderStarted = new TaskCompletionSource();
        var releaseRender = new TaskCompletionSource();
        var gateCalls = 0;

        queued.Gate.EvaluateAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                // The first pass runs straight through; the second is held here, past the memo it
                // has already read as empty.
                if (Interlocked.Increment(ref gateCalls) == 1)
                    return PlaybackGateResult.Ok;

                secondAtTheGate.TrySetResult();
                await releaseSecond.Task;
                return PlaybackGateResult.Ok;
            });

        queued.Preparer.PrepareAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                renderStarted.TrySetResult();
                await releaseRender.Task;
                return false;
            });

        queued.Queue();

        var first = queued.Service.ReconcileAsync();
        await renderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = queued.Service.ReconcileAsync();
        await secondAtTheGate.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The first pass now finishes and records the failure, behind the second pass's back.
        releaseRender.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        releaseSecond.SetResult();
        await second.WaitAsync(TimeSpan.FromSeconds(5));

        await queued.Preparer.Received(1).PrepareAsync(
            queued.Source, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The cheap check runs first. Behind the gate it meant an already-rendered song
    /// paying a probe, or a whole kit read, every time anybody touched the queue.</summary>
    [Fact]
    public async Task ASongThatIsAlreadyRendered_AsksNobodyAnything()
    {
        await using var queued = new QueuedKit();

        queued.LandTheRender();
        queued.Gate.ClearReceivedCalls();

        await queued.Service.ReconcileAsync();

        await queued.Gate.DidNotReceive().EvaluateAsync(
            Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>());
        await queued.Preparer.DidNotReceive().PrepareAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A burst of announcements collapses rather than starting a pass each. A bulk enqueue
    /// raises three announcements per song, and every pass walks the whole queue.</summary>
    /// <remarks>The first pass is held open deliberately. Coalescing merges arrivals that overlap,
    /// so a burst of passes that each finish in microseconds would run one after another and prove
    /// nothing, which is how an earlier version of this test passed and then failed only when the
    /// suite was busy enough to change the timing.</remarks>
    [Fact]
    public async Task ManyOverlappingReconciles_DoNotEachWalkTheQueue()
    {
        var folder = Directory.CreateTempSubdirectory("khost-coalesce-");

        try
        {
            var reached = new TaskCompletionSource();
            var release = new TaskCompletionSource();

            var performances = Substitute.For<IPerformanceService>();
            performances.ReadQueuedAsync().Returns(async _ =>
            {
                reached.TrySetResult();
                await release.Task;
                return new List<Performance>();
            });

            var services = Substitute.For<IServiceProvider>();
            services.GetService(typeof(IPerformanceService)).Returns(performances);
            services.GetService(typeof(IMediaService)).Returns(Substitute.For<IMediaService>());
            services.GetService(typeof(IPlaybackService)).Returns(Substitute.For<IPlaybackService>());

            var service = Service(folder, services);

            // The constructor's own pass is now inside ReadQueuedAsync and holding the gate.
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));

            const int bursts = 20;
            var burst = Enumerable.Range(0, bursts).Select(_ => service.ReconcileCoalescedAsync()).ToList();

            release.SetResult();
            await Task.WhenAll(burst).WaitAsync(TimeSpan.FromSeconds(5));

            var walks = performances.ReceivedCalls()
                .Count(call => call.GetMethodInfo().Name == nameof(IPerformanceService.ReadQueuedAsync));

            // Two passes' worth: the one already running, plus exactly one allowed to queue behind
            // it, with the other nineteen collapsed into that one. Below this range the arrivals
            // during the running pass were dropped instead of earning a follow-up; above it, every
            // arrival started its own pass.
            Assert.InRange(walks, 3, bursts - 1);
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>The whole reason a plugin's format is rendered: the finished file is moved into
    /// place and the queue row is told, which is the only way it learns the turn can start.</summary>
    [Fact]
    public async Task APluginsRenderThatSucceeds_IsMovedIntoPlaceAndAnnounced()
    {
        // A preparer that actually produces the file, which no other test here does.
        await using var queued = new QueuedKit(render: (_, working) =>
        {
            File.WriteAllText(working, "rendered");
            return true;
        });

        await queued.Service.ReconcileAsync();

        Assert.Single(queued.Renders());
        Assert.Equal(PerformancePreparation.Prepared, queued.Service.StateFor(queued.Source));

        // Twice: once as the render starts, so the row can show a spinner, and once when it lands,
        // which is the only way the row learns the turn can start. Counting matters, since the
        // first announce alone would satisfy a bare Received().
        queued.Broker.Received(2).Announce(Arg.Any<PreparedMediaChanged>());
    }

    /// <summary>A turn whose render is in flight is preparing, not unprepared: that is what puts
    /// the spinner on the row and greys its play control.</summary>
    [Fact]
    public async Task ARenderInFlight_ReportsPreparing()
    {
        await using var queued = new QueuedKit();

        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        queued.Preparer.PrepareAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                started.TrySetResult();
                await release.Task;
                return false;
            });

        var reconcile = queued.Service.ReconcileAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PerformancePreparation.Preparing, queued.Service.StateFor(queued.Source));

        release.SetResult();
        await reconcile.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>A plugin's format is rendered whatever its tracks would do, because there is no
    /// transcode to fall back on: the mixable check is only for files the host can already play.
    /// </summary>
    [Fact]
    public async Task APluginsFormatWithMixableTracks_IsStillRendered()
    {
        await using var queued = new QueuedKit();

        queued.Tracks.ReadTracksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<AudioTrack>>(
            [
                new AudioTrack(0, AudioTrackRole.Music, "Instrumental"),
                new AudioTrack(1, AudioTrackRole.Lead, "Lead Vocal"),
            ]);

        await queued.Service.ReconcileAsync();

        await queued.Preparer.Received(1).PrepareAsync(
            queued.Source, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A row imported without a duration leaves nothing to measure a render against, and
    /// a render is named for its source, so a short one accepted here is served for the life of
    /// that file. The source knows how long it is even when the row does not.</summary>
    [Fact]
    public async Task ARenderOfARowWithNoDuration_IsMeasuredAgainstTheSource()
    {
        await using var queued = new QueuedKit(render: (_, working) =>
        {
            File.WriteAllText(working, "rendered");
            return true;
        });

        // The row carries no duration, so without asking the source there is nothing to check.
        queued.Probes.ProbeAsync(queued.Source, Arg.Any<CancellationToken>())
            .Returns(new MediaProbeResult { Duration = TimeSpan.FromMinutes(4) });

        await queued.Service.ReconcileAsync();

        await queued.Probes.Received().ProbeAsync(queued.Source, Arg.Any<CancellationToken>());
    }

    /// <summary>Naming a render reads the source's size and write time, which throws when the file
    /// is gone. That is a file deleted between a caller's check and this line, and it is reached
    /// from a queue row's render ahead of the load's own try, where the row catches only
    /// KHostException: thrown from there it takes the Blazor circuit down over a song nobody could
    /// have played anyway.</summary>
    [Fact]
    public void NamingARenderForAFileThatIsGone_AnswersNullRatherThanThrowing()
        => Assert.Null(Service().PathFor("/nowhere/at/all/song.mp4"));

    /// <summary>And the callers turn that into the ordinary "no render" answer.</summary>
    [Fact]
    public void AFileThatVanishes_AnswersUnpreparedRatherThanThrowing()
    {
        var folder = Directory.CreateTempSubdirectory("khost-vanish-");

        try
        {
            var source = Path.Combine(folder.FullName, "song.mp4");
            File.WriteAllText(source, "x");

            var service = Service(folder);

            // Gone after the row was drawn, which is the window this is about.
            File.Delete(source);

            Assert.Equal(PerformancePreparation.Unprepared, service.StateFor(source));
            Assert.Null(service.TryResolve(source));
            Assert.False(service.IsWaitingOnARender(source));
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>Past the budget a song plays the way it always did. The pre-render is an
    /// optimisation and must never be the reason a machine runs out of disk in front of a room.
    /// </summary>
    [Fact]
    public async Task ARenderPastTheBudget_IsNotStarted()
    {
        await using var queued = new QueuedKit(budgetMegabytes: 1);

        // A megabyte of renders already held, which is the whole budget.
        queued.FillRenders(bytes: 1024 * 1024);

        await queued.Service.ReconcileAsync();

        await queued.Preparer.DidNotReceive().PrepareAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The control: the same queue renders when there is room, so the test above is
    /// reading the budget rather than a reconcile that never reached the render.</summary>
    [Fact]
    public async Task ARenderWithinTheBudget_IsStarted()
    {
        await using var queued = new QueuedKit(budgetMegabytes: 1);

        await queued.Service.ReconcileAsync();

        await queued.Preparer.Received(1).PrepareAsync(
            queued.Source, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Zero is the escape hatch for a venue that would rather manage its own disk.</summary>
    [Fact]
    public async Task ABudgetOfZero_LiftsTheCap()
    {
        await using var queued = new QueuedKit(budgetMegabytes: 0);

        queued.FillRenders(bytes: 4 * 1024 * 1024);

        await queued.Service.ReconcileAsync();

        await queued.Preparer.Received(1).PrepareAsync(
            queued.Source, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A clock for a render that is no longer there is never revisited by the drop loop,
    /// so it would sit in the dictionary for the life of the process.</summary>
    [Fact]
    public async Task AGraceClockForARenderThatIsGone_IsForgotten()
    {
        await using var queued = new QueuedKit();

        var render = queued.LandRenderFor("ghost");

        // Let go of once, so a clock starts against it.
        await queued.Service.ReconcileAsync();
        Assert.True(queued.Service.HasGraceClockFor(render));

        // Removed by something other than the drop loop, which is what the startup sweep and a
        // host tidying the folder by hand both look like.
        File.Delete(render);
        await queued.Service.ReconcileAsync();

        Assert.False(queued.Service.HasGraceClockFor(render));
    }

    /// <summary>The rule the gate exists for: licensed content must never leave its provider's
    /// container for an account that is not entitled to it. Refused here, no playable copy is ever
    /// written, rather than one existing on disk and being refused at the microphone.</summary>
    [Fact]
    public async Task AQueuedSongItsProviderRefuses_IsNeverRendered()
    {
        await using var queued = new QueuedKit(verdict: new PlaybackGateResult(false, "Sign in to the provider."));

        await queued.Service.ReconcileAsync();

        await queued.Preparer.DidNotReceive().PrepareAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(queued.Renders());
    }

    /// <summary>The control for the refusal above: without it, a test asserting nothing was
    /// rendered passes just as well when the reconcile never reaches the render at all.</summary>
    [Fact]
    public async Task AQueuedSongItsProviderAllows_IsRendered()
    {
        await using var queued = new QueuedKit();

        await queued.Service.ReconcileAsync();

        await queued.Preparer.Received(1).PrepareAsync(
            queued.Source, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A gate is asked what the host is about to do, and here that is always the render.
    /// Passing the wrong moment asks a question whose answer is about something else: Example
    /// prompts for a sign-in on a queue and a play, and there is nobody watching a render.</summary>
    [Fact]
    public async Task PreparingASong_AsksTheGateAboutRendering()
    {
        await using var queued = new QueuedKit();

        await queued.Service.ReconcileAsync();

        await queued.Gate.Received().EvaluateAsync(
            MediaAction.Render, Arg.Any<Media>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A gate that throws must not stop the rest of the queue being made ready, so the
    /// render goes ahead: a provider's bug is not a reason to strand every other singer.</summary>
    [Fact]
    public async Task AGateThatThrows_LetsTheRenderProceed()
    {
        await using var queued = new QueuedKit();

        queued.Gate.EvaluateAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>())
            .Returns<PlaybackGateResult>(_ => throw new InvalidOperationException("the plugin fell over"));

        await queued.Service.ReconcileAsync();

        await queued.Preparer.Received(1).PrepareAsync(
            queued.Source, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>One queued turn against a file only a plugin can open, with a gate that allows it
    /// and a preparer that claims it. The render never really runs: the preparer is a substitute,
    /// so what these read is whether it was asked.</summary>
    private sealed class QueuedKit : IAsyncDisposable
    {
        private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("khost-render-gate-");
        private readonly string _root;

        /// <remarks>Everything is arranged before the service is built, and the service is built
        /// last. Its constructor starts a reconcile that nothing awaits, so a substitute stubbed
        /// after construction can be reached first by that background pass with its default
        /// behaviour, which then lands in the failure memo and skips the render this test is
        /// about. That is why these take their behaviour here rather than being re-stubbed.
        /// </remarks>
        private readonly List<Performance> _queue = [];

        public QueuedKit(
            Func<string, string, bool>? render = null,
            PlaybackGateResult? verdict = null,
            int budgetMegabytes = 0,
            bool queued = true)
        {
            Source = Path.Combine(_folder.FullName, "song.kit");
            File.WriteAllText(Source, "x");

            Preparer = Substitute.For<IMediaPreparer>();
            Preparer.CanPrepare(Source).Returns(true);
            Preparer.PrepareAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => render?.Invoke(call.ArgAt<string>(0), call.ArgAt<string>(1)) ?? false);

            Tracks = Substitute.For<IAudioTrackService>();
            Probes = Substitute.For<IMediaProbeService>();
            Broker = Substitute.For<IMessageBroker>();

            Gate = Substitute.For<IMediaGateService>();
            Gate.EvaluateAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>())
                .Returns(verdict ?? PlaybackGateResult.Ok);

            var mediaId = Guid.NewGuid();
            var media = Substitute.For<IMediaService>();
            media.ReadAsync(mediaId).Returns(_ => new Media { Id = mediaId, FilePath = Source, Title = "Song" });

            _turn = new Performance { MediaId = mediaId, SingerId = Guid.NewGuid() };
            if (queued) _queue.Add(_turn);

            Performances = Substitute.For<IPerformanceService>();
            Performances.ReadQueuedAsync().Returns(_ => _queue.ToList());

            var services = Substitute.For<IServiceProvider>();
            services.GetService(typeof(IPerformanceService)).Returns(Performances);
            services.GetService(typeof(IMediaService)).Returns(media);
            services.GetService(typeof(IPlaybackService)).Returns(Substitute.For<IPlaybackService>());
            services.GetService(typeof(IMediaGateService)).Returns(Gate);
            services.GetService(typeof(IEnumerable<IMediaPreparer>)).Returns(new[] { Preparer });
            services.GetService(typeof(IAudioTrackService)).Returns(Tracks);
            services.GetService(typeof(IMediaProbeService)).Returns(Probes);

            Service = PreparedMediaServiceTests.Service(
                _folder, services, broker: Broker, budgetMegabytes: budgetMegabytes);
            _root = Path.Combine(_folder.FullName, "prepared");
        }

        public string Source { get; }

        private readonly Performance _turn;

        public IMediaPreparer Preparer { get; }

        public IMediaGateService Gate { get; }

        public IPerformanceService Performances { get; }

        public IAudioTrackService Tracks { get; }

        public IMediaProbeService Probes { get; }

        public IMessageBroker Broker { get; }

        public PreparedMediaService Service { get; }

        /// <summary>Whatever landed, found by extension: the service names a render for the source
        /// file's hash, so a path spelled out here would miss it and read as nothing rendered.
        /// </summary>
        public string[] Renders()
            => Directory.Exists(_root) ? Directory.GetFiles(_root, "*.khv") : [];

        /// <summary>Stands in for a finished render, under the name the service resolves by.</summary>
        public void LandTheRender()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, NameFor(Source)), "rendered");
        }

        /// <summary>Settles the pass the service's constructor starts, so a test arranging a
        /// substitute afterwards is not racing it. Three tests here failed intermittently, only on
        /// a loaded machine, for want of this.</summary>
        public Task ReadyAsync() => Service.StartupReconcile;

        /// <summary>Puts the turn on the queue, for a test that started with it empty.</summary>
        public void Queue() => _queue.Add(_turn);

        /// <summary>A render for some other song, which the queue does not want.</summary>
        public string LandRenderFor(string name)
        {
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, $"{name}.khv");
            File.WriteAllText(path, "rendered");
            return path;
        }

        /// <summary>Renders already held, to put the directory over a budget.</summary>
        public void FillRenders(int bytes)
        {
            Directory.CreateDirectory(_root);
            File.WriteAllBytes(Path.Combine(_root, "held.khv"), new byte[bytes]);
        }

        public ValueTask DisposeAsync()
        {
            _folder.Delete(recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Stands in for a finished render, named the way the service names one.</summary>
    private static string RenderFor(PreparedMediaService service, string source, DirectoryInfo working)
    {
        var root = Path.Combine(working.FullName, "prepared");
        Directory.CreateDirectory(root);

        // The name is the service's own, read back through the state it reports.
        var render = Directory.GetFiles(root, "*.khv").FirstOrDefault();
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
            System.Text.Encoding.UTF8.GetBytes(seed))) + ".khv";
    }

    /// <summary>Two singers queueing the same song, or two reconciles landing together, must not
    /// start two renders: both would write the same file and neither would be whole.</summary>
    [Fact]
    public async Task TheSameFileQueuedTwiceAtOnce_RendersOnce()
    {
        var folder = Directory.CreateTempSubdirectory("khost-once-");

        try
        {
            var source = Path.Combine(folder.FullName, "song.mp4");
            File.WriteAllText(source, "x");

            var mediaId = Guid.NewGuid();
            var media = Substitute.For<IMediaService>();
            media.ReadAsync(mediaId).Returns(_ => new Media { Id = mediaId, FilePath = source, Title = "Song" });

            var performances = Substitute.For<IPerformanceService>();

            // Two singers, one song: the queue holds two turns against the same file.
            performances.ReadQueuedAsync().Returns(_ =>
            [
                new Performance { MediaId = mediaId, SingerId = Guid.NewGuid() },
                new Performance { MediaId = mediaId, SingerId = Guid.NewGuid() },
            ]);

            // A preparer claims the file so the render never reaches ffmpeg. Without one this
            // started a real encode, which KHost.UnitTests is meant never to do, and the count
            // below is what the test is actually about.
            var renders = 0;
            var preparer = Substitute.For<IMediaPreparer>();
            preparer.CanPrepare(source).Returns(true);
            preparer.PrepareAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    Interlocked.Increment(ref renders);
                    return false;
                });

            var services = Substitute.For<IServiceProvider>();
            services.GetService(typeof(IPerformanceService)).Returns(performances);
            services.GetService(typeof(IMediaService)).Returns(media);
            services.GetService(typeof(IPlaybackService)).Returns(Substitute.For<IPlaybackService>());
            services.GetService(typeof(IEnumerable<IMediaPreparer>)).Returns(new[] { preparer });

            var service = Service(folder, services);

            // Several reconciles at once, which is what a burst of queue changes looks like.
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => service.ReconcileAsync()));

            // Two turns against one file, four passes over them: one render. Counting is the
            // assertion, since a leftover .part is cleaned up on every path whether or not the
            // work was deduplicated at all.
            Assert.Equal(1, renders);

            var root = Path.Combine(folder.FullName, "prepared");
            Assert.Empty(Directory.Exists(root) ? Directory.GetFiles(root, "*.part") : []);
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>A render is named for its source, so a truncated one is indistinguishable from a
    /// whole one and would be served for the rest of that file's life.</summary>
    [Fact]
    public void ARenderThatStopsEarly_IsNotWhole()
        => Assert.False(PreparedMediaService.IsLongEnough(TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(1)));

    [Fact]
    public void ARenderThatCoversTheSong_IsWhole()
        => Assert.True(PreparedMediaService.IsLongEnough(TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(4)));

    /// <summary>A .cdg stops drawing before its audio ends, so the picture runs out first. Measured
    /// at 3.4s short on a real pair; refusing that would cost every CDG song its copy.</summary>
    [Fact]
    public void ARenderAFewSecondsShort_IsStillWhole()
        => Assert.True(PreparedMediaService.IsLongEnough(TimeSpan.FromSeconds(230.7), TimeSpan.FromSeconds(227.3)));

    /// <summary>A row with no duration is not evidence the render is short, and refusing it would
    /// cost the song its copy for no reason.</summary>
    [Fact]
    public void ASongOfUnknownLength_IsNeverRefused()
        => Assert.True(PreparedMediaService.IsLongEnough(null, TimeSpan.FromSeconds(1)));

    [Fact]
    public void ASongRecordedAsZeroLength_IsNeverRefused()
        => Assert.True(PreparedMediaService.IsLongEnough(TimeSpan.Zero, TimeSpan.FromSeconds(1)));

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
        DirectoryInfo? working = null, IServiceProvider? services = null, TimeSpan? grace = null,
        IMessageBroker? broker = null, int budgetMegabytes = 0)
        => new(
            NullLogger<PreparedMediaService>.Instance,
            Options.Create(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251/",
                WorkingDirectory = (working ?? Directory.CreateTempSubdirectory("khost-state-root-")).FullName,
                PreparedBudgetMegabytes = budgetMegabytes,

                // Off by default here: a build machine's free space is not this test's business.
                PreparedFreeSpaceFloorMegabytes = 0,
            }),
            services ?? Substitute.For<IServiceProvider>(),
            broker ?? Substitute.For<IMessageBroker>())
        {
            KeepAfterUnwanted = grace ?? TimeSpan.FromMinutes(5),
        };
}
