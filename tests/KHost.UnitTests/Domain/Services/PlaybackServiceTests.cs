using KHost.Plugins.Sdk.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;

namespace KHost.UnitTests.Domain.Services;

public class PlaybackServiceTests : IDisposable
{
    private readonly ILogger<PlaybackService> _logger = Substitute.For<ILogger<PlaybackService>>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly ISingerQueueService _queueService = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performanceService = Substitute.For<IPerformanceService>();
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly IMediaStreamService _mediaStreams = Substitute.For<IMediaStreamService>();
    private readonly ICastService _cast = Substitute.For<ICastService>();
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();

    // Real: a substitute would make the IsPrimary assertions below test nothing.
    private readonly ScreenCoordinationService _screenCoordination;
    private readonly PlaybackService _service;
    private int _streamsOpened;

    public PlaybackServiceTests()
    {
        // NSubstitute returns string.Empty for unstubbed strings, so "no receiver" must be said.
        _cast.ConnectedDeviceId.Returns((string?)null);

        _screenCoordination = new ScreenCoordinationService(NullLogger<ScreenCoordinationService>.Instance, _screenServer, Substitute.For<IVenuesService>(), _broker);

        _venuesService.ReadSelectedVenueAsync()
            .Returns(new Venue { Id = Guid.NewGuid(), Name = "Test Venue", Settings = new Venue.VenueSettings() });

        // Playback refuses to start with no screen attached, so the default fixture has one.
        ConnectScreens(1);

        _mediaStreams
            .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new MediaStreamSession
            {
                Id = $"stream-{Interlocked.Increment(ref _streamsOpened)}",
                SourcePath = call.ArgAt<string>(0),
                PlaylistUrl = $"http://host/media/stream-{_streamsOpened}/stream.m3u8",
                StartOffset = call.ArgAt<TimeSpan>(1),
                PitchSemitones = call.ArgAt<int>(2),
            });

        // Zero fade keeps stop synchronous; the fading behaviour has its own tests below.
        _service = MakeService(TimeSpan.Zero);
    }

    private void ConnectScreens(int count, bool supportsSync = true)
    {
        var screens = Enumerable.Range(1, count).Select(i =>
        {
            var screen = Substitute.For<IScreenConnection>();
            screen.ScreenId.Returns($"Screen {i}");
            screen.ConnectionId.Returns($"conn-{i}");
            screen.IsConnected.Returns(true);
            // Audio and video as well as sync: a Photino screen declares all three, and the
            // background channel only goes to a screen that can carry the room's audio.
            screen.Capabilities.Returns(new ScreenCapabilities
            {
                SupportsSync = supportsSync,
                SupportsAudio = true,
                SupportsVideo = true,
            });
            return screen;
        }).ToArray();

        _screenServer.GetConnectedScreensAsync().Returns(_ => ToAsyncEnumerable(screens));
    }

    /// <summary>Mixed group: sync-capable screens plus loose consumers such as a Cast device.</summary>
    private void ConnectMixedScreens()
    {
        var synced = Substitute.For<IScreenConnection>();
        synced.ScreenId.Returns("Screen 1");
        synced.ConnectionId.Returns("conn-1");
        synced.IsConnected.Returns(true);
        synced.Capabilities.Returns(new ScreenCapabilities { SupportsSync = true });

        var loose = Substitute.For<IScreenConnection>();
        loose.ScreenId.Returns("Chromecast");
        loose.ConnectionId.Returns("conn-cast");
        loose.IsConnected.Returns(true);
        loose.Capabilities.Returns(ScreenCapabilities.None);

        _screenServer.GetConnectedScreensAsync().Returns(_ => ToAsyncEnumerable([synced, loose]));
    }

    private static async IAsyncEnumerable<IScreenConnection> ToAsyncEnumerable(IScreenConnection[] screens)
    {
        foreach (var screen in screens)
            yield return screen;

        await Task.CompletedTask;
    }

    private PlaybackService MakeService(TimeSpan stopFadeDuration) => new(
        _logger,
        _queueService,
        _performanceService,
        _venuesService,
        Substitute.For<IAnalyticsService>(),
        _screenServer,
        _mediaStreams,
        _screenCoordination,
        _cast,
        _breakMusic,
        _mediaService,
        Options.Create(new PlaybackService.ServiceOptions { StopFadeDuration = stopFadeDuration }),
        _broker);

    public void Dispose() => _service.Dispose();

    [Fact]
    public void NewService_StartsStopped()
    {
        Assert.Equal(PlaybackState.Stopped, _service.State);
        Assert.Null(_service.CurrentPerformance);
        Assert.Equal(TimeSpan.Zero, _service.Position);
    }

    /// <summary>
    /// A load that throws leaves the console wedged unless it puts its state back: remove is
    /// disabled for the current performance, play for every row, and stop for a state that never
    /// reached Playing, so the host cannot clear the song that failed.
    /// </summary>
    private void FailTheStreamOpen() => _mediaStreams
        .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
        .Returns<MediaStreamSession>(_ => throw new FileNotFoundException("Media file not found: /gone.cdg"));

    [Fact]
    public async Task LoadAsync_TheStreamWillNotOpen_TellsTheHost()
    {
        var (performance, media) = CreatePerformance();

        FailTheStreamOpen();

        var error = await Assert.ThrowsAsync<KHostException>(() => _service.LoadAsync(performance, media));

        Assert.Equal("KH-STREAM-OPEN", error.ReferenceCode);
    }

    [Fact]
    public async Task LoadAsync_TheStreamWillNotOpen_ClearsTheCurrentPerformance()
    {
        var (performance, media) = CreatePerformance();

        FailTheStreamOpen();

        await Assert.ThrowsAsync<KHostException>(() => _service.LoadAsync(performance, media));

        // Left set, this disables the row's own remove button and every row's play button.
        Assert.Null(_service.CurrentPerformance);
        Assert.Null(_service.CurrentMedia);
    }

    [Fact]
    public async Task LoadAsync_TheStreamWillNotOpen_UnlocksTheTopSlot()
    {
        var (performance, media) = CreatePerformance();

        FailTheStreamOpen();

        await Assert.ThrowsAsync<KHostException>(() => _service.LoadAsync(performance, media));

        _queueService.Received(1).UnlockTopSlot();
    }

    [Fact]
    public async Task LoadAsync_TheStreamWillNotOpen_BringsBreakMusicBack()
    {
        var (performance, media) = CreatePerformance();

        FailTheStreamOpen();

        await Assert.ThrowsAsync<KHostException>(() => _service.LoadAsync(performance, media));

        // Suspended on the way in, so a failed load that does not restore it leaves the room silent.
        await _breakMusic.Received(1).RestoreAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_TheStreamWillNotOpen_LeavesTheSongQueuedForTheSameSinger()
    {
        var (performance, media) = CreatePerformance();

        FailTheStreamOpen();

        await Assert.ThrowsAsync<KHostException>(() => _service.LoadAsync(performance, media));

        // Nothing was performed, so the singer keeps their turn and the song stays in the queue.
        await _performanceService.DidNotReceive().DequeueAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
        await _queueService.DidNotReceive().RotateQueueAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task LoadAsync_TheStreamWillNotOpen_RedrawsTheConsole()
    {
        var (performance, media) = CreatePerformance();

        FailTheStreamOpen();

        var raised = 0;
        using var subscription = _broker.Subscribe<PlaybackChanged>(_ => raised++);

        await Assert.ThrowsAsync<KHostException>(() => _service.LoadAsync(performance, media));

        // Without this the panels keep rendering the wedged state they were last told about.
        Assert.True(raised > 0, "the console was never told the load was abandoned");
    }

    [Fact]
    public async Task LoadAsync_AfterAFailedLoad_TheNextLoadStillWorks()
    {
        var (failed, failedMedia) = CreatePerformance();

        FailTheStreamOpen();

        await Assert.ThrowsAsync<KHostException>(() => _service.LoadAsync(failed, failedMedia));

        // The stream opens again, as it would for a different song.
        _mediaStreams
            .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new MediaStreamSession
            {
                Id = "stream-recovered",
                SourcePath = call.ArgAt<string>(0),
                PlaylistUrl = "http://host/media/stream-recovered/stream.m3u8",
                StartOffset = call.ArgAt<TimeSpan>(1),
                PitchSemitones = call.ArgAt<int>(2),
            });

        var (next, nextMedia) = CreatePerformance();

        await _service.LoadAsync(next, nextMedia);

        Assert.Equal(next.Id, _service.CurrentPerformance?.Id);
    }

    [Fact]
    public async Task Load_SetsCurrentPerformanceAndMedia_AndResetsPosition()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);

        Assert.Same(performance, _service.CurrentPerformance);
        Assert.Same(media, _service.CurrentMedia);
        Assert.Equal(TimeSpan.Zero, _service.Position);
        Assert.Equal(PlaybackState.Stopped, _service.State);
    }

    [Fact]
    public async Task Load_AnnouncesPlaybackChanged()
    {
        var raised = false;
        using var subscription = _broker.Subscribe<PlaybackChanged>(_ => raised = true);

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        Assert.True(raised);
    }

    [Theory]
    [InlineData(MediaStatus.Downloading)]
    [InlineData(MediaStatus.Broken)]
    [InlineData(MediaStatus.Processing)]
    [InlineData(MediaStatus.Unknown)]
    public async Task LoadAsync_RefusesMediaThatIsNotReady(MediaStatus status)
    {
        var (performance, media) = CreatePerformance();
        media.Status = status;

        await _service.LoadAsync(performance, media);

        Assert.Null(_service.CurrentPerformance);
        Assert.Null(_service.CurrentMedia);
        await _queueService.DidNotReceive().MoveUserToStartAsync(Arg.Any<Guid>());
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<LoadMediaCommand>());
    }

    [Fact]
    public async Task LoadAsync_ReadyMedia_Loads()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);

        Assert.Same(performance, _service.CurrentPerformance);
    }

    [Fact]
    public async Task PlayAsync_DoesNothing_WhenNoMediaLoaded()
    {
        await _service.PlayAsync();

        Assert.Equal(PlaybackState.Stopped, _service.State);
        await _queueService.DidNotReceive().MoveUserToStartAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task PlayAsync_TransitionsToPlaying_AndMarksUserPerforming()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        Assert.Equal(PlaybackState.Playing, _service.State);
        Assert.Equal(performance.SingerId, _service.CurrentlyPerformingUserId);
    }

    [Fact]
    public async Task PlayAsync_MovesUserToStart()
    {
        var (performance, media) = CreatePerformance();
        var user = new KHostUser { Id = performance.SingerId, Name = "Alice" };
        _queueService.Users.Returns(new[] { user }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _queueService.Received(1).MoveUserToStartAsync(performance.SingerId);
    }

    [Fact]
    public async Task PlayAsync_IsNoOp_WhenAlreadyPlaying()
    {
        var (performance, media) = CreatePerformance();
        var user = new KHostUser { Id = performance.SingerId, Name = "Alice" };
        _queueService.Users.Returns(new[] { user }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        _queueService.ClearReceivedCalls();

        await _service.PlayAsync();

        await _queueService.DidNotReceive().MoveUserToStartAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Pause_TransitionsToPaused_AndRetainsPerformingId()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.PauseAsync();

        Assert.Equal(PlaybackState.Paused, _service.State);
        Assert.Equal(performance.SingerId, _service.CurrentlyPerformingUserId);
    }

    [Fact]
    public async Task Pause_IsNoOp_WhenNotPlaying()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        await _service.PauseAsync();

        Assert.Equal(PlaybackState.Stopped, _service.State);
    }

    [Fact]
    public async Task StopAsync_ResetsStateAndClearsCurrentMedia()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.StopAsync();

        Assert.Equal(PlaybackState.Stopped, _service.State);
        Assert.Null(_service.CurrentPerformance);
        Assert.Null(_service.CurrentlyPerformingUserId);
        Assert.Equal(TimeSpan.Zero, _service.Position);
    }

    [Fact]
    public async Task Load_ResetsPerforming_OfPreviousUser()
    {
        var (perf1, media1) = CreatePerformance();
        var (perf2, media2) = CreatePerformance();

        await _service.LoadAsync(perf1, media1);
        await _service.PlayAsync();

        await _service.LoadAsync(perf2, media2);

        Assert.Null(_service.CurrentlyPerformingUserId);
        Assert.Same(perf2, _service.CurrentPerformance);
    }

    [Fact]
    public async Task StopAsync_RotatesQueueForFinishedSinger()
    {
        var (performance, media) = CreatePerformance();
        var user = new KHostUser { Id = performance.SingerId, Name = "Alice" };
        _queueService.Users.Returns(new[] { user }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.StopAsync();

        // Which rotation applies is the queue service's call; playback only reports who finished.
        await _queueService.Received().RotateQueueAsync(performance.SingerId);
        await _performanceService.Received().DequeueAsync(performance.SingerId, performance.Id);
    }

    [Fact]
    public async Task StopAsync_DequeuesPerformance()
    {
        var (performance, media) = CreatePerformance();
        var user = new KHostUser { Id = performance.SingerId, Name = "Alice" };
        _queueService.Users.Returns(new[] { user }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.StopAsync();

        await _performanceService.Received(1).DequeueAsync(performance.SingerId, performance.Id);
    }

    [Fact]
    public async Task StopAsync_DoesNotCallMoveUserToEnd_WhenMoveToBottomDisabled()
    {
        var (performance, media) = CreatePerformance();
        var user = new KHostUser { Id = performance.SingerId, Name = "Alice" };
        _queueService.Users.Returns(new[] { user }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.StopAsync();

        await _queueService.DidNotReceive().MoveUserToEndAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task StopAsync_DoesNotCallSelectFirst_WhenMoveToBottomDisabled()
    {
        var (performance, media) = CreatePerformance();
        var user = new KHostUser { Id = performance.SingerId, Name = "Alice" };
        _queueService.Users.Returns(new[] { user }.AsReadOnly());

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.StopAsync();

        await _queueService.DidNotReceive().SelectFirstUserInQueueAsync();
    }

    [Fact]
    public async Task StopAsync_WhenNothingLoaded_DoesNotCallDequeue()
    {
        await _service.StopAsync();

        await _performanceService.DidNotReceive().DequeueAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
    }


    [Fact]
    public async Task PlayAsync_BroadcastsPlayCommand()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _screenServer.Received(1).BroadcastCommandAsync(Arg.Any<PlayCommand>());
    }

    [Fact]
    public async Task StopAsync_BroadcastsStopCommand()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.StopAsync();

        await _screenServer.Received(1).BroadcastCommandAsync(Arg.Any<StopCommand>());
    }

    [Fact]
    public async Task HasConnectedScreenAsync_IsTrue_WhenAScreenIsAttached()
    {
        Assert.True(await _service.HasConnectedScreenAsync());
    }

    [Fact]
    public async Task HasConnectedScreenAsync_IsFalse_WhenNoScreensAreAttached()
    {
        ConnectScreens(0);

        Assert.False(await _service.HasConnectedScreenAsync());
    }

    [Fact]
    public async Task PlayAsync_DoesNotStart_WhenNoScreensAreConnected()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        ConnectScreens(0);

        await _service.PlayAsync();

        Assert.Equal(PlaybackState.Stopped, _service.State);
        Assert.Null(_service.CurrentlyPerformingUserId);
    }

    [Fact]
    public async Task PlayAsync_DoesNotBroadcast_WhenNoScreensAreConnected()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        ConnectScreens(0);

        await _service.PlayAsync();

        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<PlayCommand>());
    }

    [Fact]
    public async Task PlayAsync_WithNoScreens_LeavesThePerformanceQueued()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        ConnectScreens(0);

        await _service.PlayAsync();

        // The position timer never starts, so nothing can run the turn out and dequeue it.
        await _performanceService.DidNotReceive().DequeueAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
        Assert.Same(performance, _service.CurrentPerformance);
    }

    [Fact]
    public async Task PlayAsync_Starts_OnceAScreenConnects()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        ConnectScreens(0);
        await _service.PlayAsync();
        Assert.Equal(PlaybackState.Stopped, _service.State);

        ConnectScreens(1);
        await _service.PlayAsync();

        Assert.Equal(PlaybackState.Playing, _service.State);
    }

    [Fact]
    public async Task HasConnectedScreenAsync_IsFalse_WhenEnumerationThrows()
    {
        _screenServer.GetConnectedScreensAsync().Returns(_ => throw new InvalidOperationException("hub down"));

        Assert.False(await _service.HasConnectedScreenAsync());
    }

    [Fact]
    public async Task ScreenDisconnect_PausesPlayback_WhenNoScreensRemain()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        ConnectScreens(0);
        RaiseScreenDisconnected();

        Assert.True(await WaitForStateAsync(PlaybackState.Paused));
        Assert.Same(performance, _service.CurrentPerformance);
    }

    [Fact]
    public async Task ScreenDisconnect_KeepsPlaying_WhenAnotherScreenRemains()
    {
        ConnectScreens(2);
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        ConnectScreens(1);
        RaiseScreenDisconnected();

        Assert.False(await WaitForStateAsync(PlaybackState.Paused));
        Assert.Equal(PlaybackState.Playing, _service.State);
    }

    [Fact]
    public async Task ScreenDisconnect_IsIgnored_WhenNotPlaying()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        ConnectScreens(0);
        RaiseScreenDisconnected();

        Assert.False(await WaitForStateAsync(PlaybackState.Paused));
        Assert.Equal(PlaybackState.Stopped, _service.State);
    }

    [Fact]
    public async Task ScreenDisconnect_AfterDispose_DoesNotPause()
    {
        var service = MakeService(TimeSpan.Zero);
        var (performance, media) = CreatePerformance();
        await service.LoadAsync(performance, media);
        await service.PlayAsync();

        service.Dispose();

        ConnectScreens(0);
        RaiseScreenDisconnected();

        // Same again: a disposed service must not react at all, so there is nothing to poll for.
        await Task.Delay(150);
        Assert.Equal(PlaybackState.Playing, service.State);
    }

    [Fact]
    public async Task ScreenReconnect_ReloadsCurrentMediaOntoTheNewScreen()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        _screenServer.ClearReceivedCalls();

        RaiseScreenConnected();

        // A screen that joins mid-session has nothing loaded; a bare PlayCommand would be rejected.
        Assert.True(await WaitForBroadcastAsync<LoadMediaCommand>());
    }

    [Fact]
    public async Task ScreenReconnect_SeeksToTheCurrentPosition()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.TickAsync();
        _screenServer.ClearReceivedCalls();

        RaiseScreenConnected();

        Assert.True(await WaitForBroadcastAsync<SeekCommand>());
    }

    [Fact]
    public async Task Play_PublishesATimeline_ToSyncCapableScreensOnly()
    {
        ConnectMixedScreens();

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _screenServer.Received().SendCommandAsync("Screen 1", Arg.Any<SetTimelineCommand>());

        // A Cast device cannot be held to a schedule, so sending it one would only invite it to try.
        await _screenServer.DidNotReceive().SendCommandAsync("Chromecast", Arg.Any<SetTimelineCommand>());
    }

    [Fact]
    public async Task Play_AnchorsTheTimelineSlightlyAhead_SoEveryScreenStartsOnTheSameInstant()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        var before = DateTime.UtcNow;
        await _service.PlayAsync();

        var timeline = LastTimeline();
        Assert.NotNull(timeline);
        Assert.True(timeline.IsPlaying);

        // Starting on arrival is what puts screens seconds apart; the anchor is the shared instant.
        Assert.True(timeline.AnchorUtc > before,
            $"anchor {timeline.AnchorUtc:O} should be ahead of {before:O}");
    }

    [Fact]
    public async Task Pause_PublishesAFrozenTimeline()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.PauseAsync();

        var timeline = LastTimeline();
        Assert.NotNull(timeline);
        Assert.False(timeline.IsPlaying);
    }

    [Fact]
    public async Task ScreenReconnect_RepublishesTheTimeline_SoTheJoinerLandsOnTheGroupPosition()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.TickAsync();
        _screenServer.ClearReceivedCalls();

        RaiseScreenConnected();
        Assert.True(await WaitForBroadcastAsync<PlayCommand>());

        // Without this the joiner starts at the top of the song while the group is mid-verse.
        var timeline = LastTimeline();
        Assert.NotNull(timeline);
        Assert.True(timeline.IsPlaying);
        Assert.True(timeline.Position > TimeSpan.Zero);
    }

    [Fact]
    public async Task PrimaryStateReports_DoNotPublishATimelinePerReport()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        _screenServer.ClearReceivedCalls();

        // A screen answers every command with a state report, so an unthrottled re-anchor turned
        // one report into a timeline into another report — a command storm that aborted play().
        for (var i = 0; i < 25; i++) RaisePrimaryState(TimeSpan.FromSeconds(i));

        var timelines = _screenServer.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IScreenServer.SendCommandAsync)
                        && c.GetArguments().ElementAtOrDefault(1) is SetTimelineCommand);

        Assert.True(timelines <= 2, $"25 reports produced {timelines} timelines");
    }

    [Fact]
    public async Task PrimaryStateReports_StillMoveThePosition_EvenWhenTheRepublishIsSkipped()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        RaisePrimaryState(TimeSpan.FromSeconds(40));
        RaisePrimaryState(TimeSpan.FromSeconds(41));

        // Throttling the republish must not throttle following the primary.
        Assert.InRange(_service.Position, TimeSpan.FromSeconds(40.5), TimeSpan.FromSeconds(41.5));
    }

    [Fact]
    public async Task Play_DuringAStopFade_ReloadsTheScreens()
    {
        var service = MakeService(TimeSpan.FromSeconds(5));

        var (performance, media) = CreatePerformance();
        await service.LoadAsync(performance, media);
        await service.PlayAsync();
        await service.TickAsync();

        var stopping = service.StopAsync();
        await WaitForAsync(() => service.State == PlaybackState.Stopping);
        _screenServer.ClearReceivedCalls();

        await service.PlayAsync();

        // The screens were told to fade out and drop the media; flipping our own state back to
        // Playing does not undo that for them, so the media has to be handed back.
        Assert.Contains(_screenServer.ReceivedCalls(),
            c => c.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync)
                 && c.GetArguments().FirstOrDefault() is LoadMediaCommand);

        Assert.Equal(PlaybackState.Playing, service.State);
        await stopping;
        service.Dispose();
    }

    [Fact]
    public async Task Play_IsAllowed_WithOnlyACastReceiverConnected()
    {
        ConnectScreens(0);
        _cast.ConnectedDeviceId.Returns("Living Room TV");

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // Casting to the television with nothing else attached is the setup casting is for.
        Assert.Equal(PlaybackState.Playing, _service.State);
    }

    [Fact]
    public async Task Play_IsStillRefused_WithNoScreenAndNoReceiver()
    {
        ConnectScreens(0);
        _cast.ConnectedDeviceId.Returns((string?)null);

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // Nothing is playing the song, so the clock must not run the singer's turn away.
        Assert.Equal(PlaybackState.Stopped, _service.State);
    }

    [Fact]
    public async Task Position_FollowsTheReceiver_WhenThereIsNoPrimaryScreen()
    {
        ConnectScreens(0);
        _cast.ConnectedDeviceId.Returns("Living Room TV");

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // The receiver is seconds behind the host's own clock; the host has to take its word.
        _cast.PlaybackStatusChanged += Raise.Event<EventHandler<CastPlaybackStatus>>(_cast,
            new CastPlaybackStatus
            {
                Position = TimeSpan.FromSeconds(12),
                IsPlaying = true,
                SampledAtUtc = DateTime.UtcNow,
            });

        Assert.InRange(_service.Position, TimeSpan.FromSeconds(11.5), TimeSpan.FromSeconds(12.5));
    }

    [Fact]
    public async Task Position_IgnoresTheReceiver_WhenAPrimaryScreenIsPresent()
    {
        ConnectScreens(1);
        _cast.ConnectedDeviceId.Returns("Living Room TV");

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        _cast.PlaybackStatusChanged += Raise.Event<EventHandler<CastPlaybackStatus>>(_cast,
            new CastPlaybackStatus
            {
                Position = TimeSpan.FromMinutes(3),
                IsPlaying = true,
                SampledAtUtc = DateTime.UtcNow,
            });

        // A primary's reports are timestamped against a measured clock offset; a receiver's are
        // only timestamped on arrival, so the better clock wins.
        Assert.True(_service.Position < TimeSpan.FromSeconds(5), $"position jumped to {_service.Position}");
    }

    [Fact]
    public async Task Playback_IsMirroredToAConnectedCastReceiver()
    {
        _cast.ConnectedDeviceId.Returns("Living Room TV");

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // A receiver is not a screen, so nothing broadcasts to it — playback has to drive it.
        await _cast.Received(1).LoadAsync(
            "http://host/media/stream-1/stream.m3u8", TimeSpan.Zero, Arg.Any<CancellationToken>());
        await _cast.Received(1).PlayAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Playback_TouchesNothing_WhenNoCastReceiverIsConnected()
    {
        _cast.ConnectedDeviceId.Returns((string?)null);

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.PauseAsync();

        await _cast.DidNotReceive().LoadAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _cast.DidNotReceive().PlayAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Playback_SurvivesAReceiverThatRefuses()
    {
        _cast.ConnectedDeviceId.Returns("Living Room TV");
        _cast.PlayAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("receiver went away"));

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // A television switched off mid-song must not take the performance down with it.
        Assert.Equal(PlaybackState.Playing, _service.State);
    }

    [Fact]
    public async Task Load_OpensAHostStream_AndSendsItsUrlToTheScreens()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);

        await _mediaStreams.Received(1).OpenAsync(media.FilePath, TimeSpan.Zero, 0, Arg.Any<CancellationToken>());

        var command = LastBroadcast<LoadMediaCommand>();
        Assert.NotNull(command);
        Assert.Equal("http://host/media/stream-1/stream.m3u8", command.StreamUrl);
    }

    [Fact]
    public async Task ScreenReconnect_ReusesTheRunningTranscode_RatherThanStartingASecond()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        RaiseScreenConnected();
        Assert.True(await WaitForBroadcastAsync<PlayCommand>());

        // One host transcode feeding every screen is the whole reason ffmpeg moved off the screens.
        await _mediaStreams.Received(1).OpenAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stop_ClosesTheHostStream()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.StopAsync();

        // An orphaned ffmpeg would keep transcoding a song nobody is playing.
        await _mediaStreams.Received().CloseAsync("stream-1");
    }

    [Fact]
    public async Task Load_ClosesThePreviousStream_BeforeOpeningTheNext()
    {
        var (firstPerformance, firstMedia) = CreatePerformance();
        await _service.LoadAsync(firstPerformance, firstMedia);

        var (secondPerformance, secondMedia) = CreatePerformance();
        await _service.LoadAsync(secondPerformance, secondMedia);

        await _mediaStreams.Received().CloseAsync("stream-1");
        Assert.Equal(2, _streamsOpened);
    }

    [Fact]
    public async Task Load_FailsPresentably_WhenTheTranscodeCannotStart()
    {
        _mediaStreams
            .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<MediaStreamSession>(_ => throw new FileNotFoundException("gone"));

        var (performance, media) = CreatePerformance();

        // No stream means no playback: every screen plays the host's transcode, so a load that
        // could not start one has nothing to send and must not be passed off as success.
        var error = await Assert.ThrowsAsync<KHostException>(() => _service.LoadAsync(performance, media));

        Assert.Null(LastBroadcast<LoadMediaCommand>());

        // Presentable, because this one reaches the host rather than only the log.
        Assert.Contains(media.Title, error.WhatHappened);
        Assert.NotEmpty(error.Suggestion);
        Assert.Equal("KH-STREAM-OPEN", error.ReferenceCode);
        Assert.IsType<FileNotFoundException>(error.InnerException);
    }

    [Fact]
    public async Task ScreenReconnect_WhileStillPlaying_ResumesTheScreen()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        _screenServer.ClearReceivedCalls();

        // A screen returning under the same id supersedes its own tracked connection, so the
        // stale socket's disconnect is discarded and no resume is ever pending.
        RaiseScreenConnected();

        Assert.True(await WaitForBroadcastAsync<PlayCommand>());
        Assert.Equal(PlaybackState.Playing, _service.State);
    }

    [Fact]
    public async Task ScreenReconnect_WhileStillPlaying_HoldsThePositionUntilTheScreenHasLoaded()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.TickAsync();

        var loading = new TaskCompletionSource();
        _screenServer.BroadcastCommandAsync(Arg.Any<LoadMediaCommand>()).Returns(_ => loading.Task);
        _screenServer.ClearReceivedCalls();

        RaiseScreenConnected();
        Assert.True(await WaitForBroadcastAsync<LoadMediaCommand>());

        // The load is still in flight. A clock left running here is what makes the screen resume
        // behind the UI, because the seek was aimed at where the song was when it started loading.
        var held = _service.Position;

        // Waited out rather than polled: the assertion is that the clock does *not* advance, and
        // there is no state to wait for. Comfortably longer than the tick it must outlive.
        await Task.Delay(700);
        Assert.Equal(held, _service.Position);

        loading.SetResult();
    }

    [Fact]
    public async Task ScreenReconnect_DoesNothing_WhenNoMediaIsLoaded()
    {
        RaiseScreenConnected();

        Assert.False(await WaitForBroadcastAsync<LoadMediaCommand>());
    }

    [Fact]
    public async Task ScreenDisconnect_ResumeOnReconnect_ResumesWhenAScreenReturns()
    {
        SetDisconnectBehavior(ScreenDisconnectBehavior.ResumeOnReconnect);
        await PlayThenLoseAllScreensAsync();

        Assert.True(await WaitForStateAsync(PlaybackState.Paused));

        ConnectScreens(1);
        RaiseScreenConnected();

        Assert.True(await WaitForStateAsync(PlaybackState.Playing));
    }

    [Fact]
    public async Task ScreenDisconnect_RestartFromStart_PausesAndRewinds()
    {
        SetDisconnectBehavior(ScreenDisconnectBehavior.RestartFromStart);
        var performance = await PlayThenLoseAllScreensAsync(tick: true);

        Assert.True(await WaitForStateAsync(PlaybackState.Paused));
        Assert.Equal(TimeSpan.Zero, _service.Position);
        Assert.Same(performance, _service.CurrentPerformance);
    }

    [Fact]
    public async Task ScreenDisconnect_RestartFromStart_DoesNotAutoResume()
    {
        SetDisconnectBehavior(ScreenDisconnectBehavior.RestartFromStart);
        await PlayThenLoseAllScreensAsync();
        Assert.True(await WaitForStateAsync(PlaybackState.Paused));

        ConnectScreens(1);
        RaiseScreenConnected();

        Assert.False(await WaitForStateAsync(PlaybackState.Playing));
        Assert.Equal(PlaybackState.Paused, _service.State);
    }

    [Fact]
    public async Task ScreenDisconnect_CancelPerformance_ClearsTheCurrentSong()
    {
        SetDisconnectBehavior(ScreenDisconnectBehavior.CancelPerformance);
        await PlayThenLoseAllScreensAsync();

        Assert.True(await WaitForStateAsync(PlaybackState.Stopped));

        for (var i = 0; i < 50 && _service.CurrentPerformance is not null; i++)
            await Task.Delay(10);

        Assert.Null(_service.CurrentPerformance);
        Assert.Null(_service.CurrentMedia);
    }

    [Fact]
    public async Task ScreenDisconnect_DefaultsToResume_WhenNoVenueIsSelected()
    {
        _venuesService.ReadSelectedVenueAsync().Returns((Venue?)null);
        await PlayThenLoseAllScreensAsync();

        Assert.True(await WaitForStateAsync(PlaybackState.Paused));

        ConnectScreens(1);
        RaiseScreenConnected();

        Assert.True(await WaitForStateAsync(PlaybackState.Playing));
    }

    private void SetDisconnectBehavior(ScreenDisconnectBehavior behavior) =>
        _venuesService.ReadSelectedVenueAsync().Returns(new Venue
        {
            Id = Guid.NewGuid(),
            Name = "Test Venue",
            Settings = new Venue.VenueSettings
            {
                OnScreenDisconnect = behavior,
            },
        });

    private async Task<Performance> PlayThenLoseAllScreensAsync(bool tick = false)
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        if (tick)
            await _service.TickAsync();

        ConnectScreens(0);
        RaiseScreenDisconnected();

        return performance;
    }

    private TCommand? LastBroadcast<TCommand>() where TCommand : class, IScreenCommand
        => _screenServer.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync))
            .Select(c => c.GetArguments().FirstOrDefault() as TCommand)
            .LastOrDefault(c => c is not null);

    private void RaisePrimaryState(TimeSpan position)
        => _screenServer.StateReceived += Raise.EventWith(_screenServer, new ScreenStateReceivedEventArgs
        {
            ScreenId = _screenCoordination.PrimaryScreenId!,
            State = new ScreenPlaybackState
            {
                StreamUrl = "http://192.168.1.10:5251/media/abc123/stream.m3u8",
                IsPlaying = true,
                Position = position,
                Duration = TimeSpan.FromMinutes(4),
                SampledAtUtc = DateTime.UtcNow,
            },
        });

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(10);
    }

    private SetTimelineCommand? LastTimeline()
        => _screenServer.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IScreenServer.SendCommandAsync))
            .Select(c => c.GetArguments().ElementAtOrDefault(1) as SetTimelineCommand)
            .LastOrDefault(c => c is not null);

    private async Task<bool> WaitForBroadcastAsync<TCommand>() where TCommand : IScreenCommand
    {
        for (var i = 0; i < 50; i++)
        {
            if (_screenServer.ReceivedCalls().Any(c =>
                    c.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync) &&
                    c.GetArguments().FirstOrDefault() is TCommand))
                return true;

            await Task.Delay(10);
        }

        return false;
    }

    private void RaiseScreenConnected()
    {
        var connection = Substitute.For<IScreenConnection>();
        connection.ScreenId.Returns("Screen 1");
        connection.ConnectionId.Returns("conn-1");

        _screenServer.ScreenConnected += Raise.EventWith(
            _screenServer, new ScreenConnectionEventArgs { Connection = connection });
    }

    private void RaiseScreenDisconnected()
    {
        var connection = Substitute.For<IScreenConnection>();
        connection.ScreenId.Returns("Screen 1");
        connection.ConnectionId.Returns("conn-1");

        _screenServer.ScreenDisconnected += Raise.EventWith(
            _screenServer, new ScreenConnectionEventArgs { Connection = connection });
    }

    // The disconnect handler runs detached so it cannot deadlock the hub lock.
    private async Task<bool> WaitForStateAsync(PlaybackState expected)
    {
        for (var i = 0; i < 50; i++)
        {
            if (_service.State == expected) return true;
            await Task.Delay(10);
        }

        return false;
    }

    [Fact]
    public async Task StopAsync_BroadcastsConfiguredFadeDuration()
    {
        var service = MakeService(TimeSpan.FromMilliseconds(80));
        var (performance, media) = CreatePerformance();

        await service.LoadAsync(performance, media);
        await service.PlayAsync();

        await service.StopAsync();

        await _screenServer.Received(1).BroadcastCommandAsync(
            Arg.Is<StopCommand>(c => c.FadeDuration == TimeSpan.FromMilliseconds(80)));
    }

    [Fact]
    public async Task StopAsync_BroadcastsZeroFadeDuration_WhenFadeDisabled()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.StopAsync();

        await _screenServer.Received(1).BroadcastCommandAsync(
            Arg.Is<StopCommand>(c => c.FadeDuration == TimeSpan.Zero));
    }

    [Fact]
    public async Task StopAsync_FromPaused_DoesNotFade()
    {
        var service = MakeService(TimeSpan.FromSeconds(5));
        var (performance, media) = CreatePerformance();

        await service.LoadAsync(performance, media);
        await service.PlayAsync();
        await service.PauseAsync();

        // A paused screen has no frames to fade, so this must not stall for the fade duration.
        var stop = service.StopAsync();
        await stop.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(PlaybackState.Stopped, service.State);
        Assert.Null(service.CurrentMedia);
    }

    [Fact]
    public async Task StopAsync_FromPaused_BroadcastsZeroFadeDuration()
    {
        var service = MakeService(TimeSpan.FromSeconds(5));
        var (performance, media) = CreatePerformance();

        await service.LoadAsync(performance, media);
        await service.PlayAsync();
        await service.PauseAsync();

        await service.StopAsync();

        await _screenServer.Received(1).BroadcastCommandAsync(
            Arg.Is<StopCommand>(c => c.FadeDuration == TimeSpan.Zero));
    }

    [Fact]
    public async Task StopAsync_FromStopped_DoesNotFade()
    {
        var service = MakeService(TimeSpan.FromSeconds(5));
        var (performance, media) = CreatePerformance();

        await service.LoadAsync(performance, media);

        var stop = service.StopAsync();
        await stop.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(PlaybackState.Stopped, service.State);
    }

    [Fact]
    public async Task StopAsync_EntersStoppingAndKeepsMediaVisible_WhileFading()
    {
        var service = MakeService(TimeSpan.FromMilliseconds(400));
        var (performance, media) = CreatePerformance();

        await service.LoadAsync(performance, media);
        await service.PlayAsync();

        var stop = service.StopAsync();

        // The fade is still running, so the panel must still have something to render.
        Assert.Equal(PlaybackState.Stopping, service.State);
        Assert.Same(media, service.CurrentMedia);
        Assert.Same(performance, service.CurrentPerformance);
        Assert.Equal(TimeSpan.FromMilliseconds(400), service.StopFadeDuration);

        await stop;

        Assert.Equal(PlaybackState.Stopped, service.State);
        Assert.Null(service.CurrentMedia);
        Assert.Null(service.StopFadeDuration);
    }

    [Fact]
    public async Task StopAsync_AnnouncesPlaybackChanged_WhenEnteringStopping()
    {
        var service = MakeService(TimeSpan.FromMilliseconds(200));
        var (performance, media) = CreatePerformance();

        await service.LoadAsync(performance, media);
        await service.PlayAsync();

        var changes = 0;
        using var subscription = _broker.Subscribe<PlaybackChanged>(_ => changes++);

        var stop = service.StopAsync();

        // The UI needs a render before the fade finishes, not just after.
        Assert.True(changes >= 1);

        await stop;

        Assert.True(changes >= 2);
    }

    [Fact]
    public async Task StopAsync_IsIgnored_WhenAlreadyStopping()
    {
        var service = MakeService(TimeSpan.FromMilliseconds(200));
        var (performance, media) = CreatePerformance();

        await service.LoadAsync(performance, media);
        await service.PlayAsync();

        var first = service.StopAsync();
        await service.StopAsync();

        await first;

        await _screenServer.Received(1).BroadcastCommandAsync(Arg.Any<StopCommand>());
    }

    [Fact]
    public async Task PlayAsync_DuringFade_CancelsTheStopCompletion()
    {
        var service = MakeService(TimeSpan.FromMilliseconds(300));
        var (performance, media) = CreatePerformance();

        await service.LoadAsync(performance, media);
        await service.PlayAsync();

        var stop = service.StopAsync();
        Assert.Equal(PlaybackState.Stopping, service.State);

        await service.PlayAsync();
        await stop;

        // Resuming mid-fade must not let the pending stop tear the performance down afterwards.
        Assert.Equal(PlaybackState.Playing, service.State);
        Assert.Same(media, service.CurrentMedia);
        Assert.Null(service.StopFadeDuration);
    }

    [Fact]
    public async Task PauseAsync_BroadcastsPauseCommand()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.PauseAsync();

        await _screenServer.Received(1).BroadcastCommandAsync(Arg.Any<PauseCommand>());
    }

    [Fact]
    public async Task TickAsync_AdvancesPosition()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromHours(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await Task.Delay(10);

        await _service.TickAsync();

        Assert.True(_service.Position > TimeSpan.Zero);
    }

    [Fact]
    public async Task TickAsync_EndsPlayback_WhenDurationExceeded()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMilliseconds(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await Task.Delay(10);

        await _service.TickAsync();

        Assert.Equal(PlaybackState.Stopped, _service.State);
        await _performanceService.Received().DequeueAsync(performance.SingerId, performance.Id);
    }

    [Fact]
    public async Task TickAsync_DoesNotEnd_WhenDurationNotExceeded()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromHours(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await Task.Delay(10);

        await _service.TickAsync();

        Assert.Equal(PlaybackState.Playing, _service.State);
    }

    [Fact]
    public async Task TickAsync_StopsPlayback_WhenPositionExceedsDuration()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMilliseconds(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await WaitForAsync(() => _service.State == PlaybackState.Stopped);

        Assert.Equal(PlaybackState.Stopped, _service.State);
        await _performanceService.Received().DequeueAsync(performance.SingerId, performance.Id);
    }

    [Fact]
    public async Task SeekAsync_MovesThePlayheadAndTellsTheScreens()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMinutes(4);
        await _service.LoadAsync(performance, media);

        await _service.SeekAsync(TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.FromMinutes(1), _service.Position);
        await _screenServer.Received().BroadcastCommandAsync(Arg.Any<SeekCommand>());
    }

    // A click at the very end of a progress bar lands on or past the last pixel.
    [Fact]
    public async Task SeekAsync_PastTheEnd_StopsAtTheEnd()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMinutes(4);
        await _service.LoadAsync(performance, media);

        await _service.SeekAsync(TimeSpan.FromMinutes(9));

        Assert.Equal(TimeSpan.FromMinutes(4), _service.Position);
    }

    [Fact]
    public async Task SeekAsync_BeforeTheStart_StopsAtZero()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMinutes(4);
        await _service.LoadAsync(performance, media);

        await _service.SeekAsync(TimeSpan.FromSeconds(-30));

        Assert.Equal(TimeSpan.Zero, _service.Position);
    }

    /// <summary>A song of unknown length still seeks; there is just nothing to clamp against.</summary>
    [Fact]
    public async Task SeekAsync_WithNoDuration_MovesAnyway()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = null;
        await _service.LoadAsync(performance, media);

        await _service.SeekAsync(TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(2), _service.Position);
    }

    [Fact]
    public async Task SeekAsync_WithNothingLoaded_DoesNothing()
    {
        await _service.SeekAsync(TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.Zero, _service.Position);
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<SeekCommand>());
    }

    [Fact]
    public async Task TickAsync_RaisesPositionChangedOnly_WhileTheSongIsStillPlaying()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        var positions = 0;
        var states = 0;
        _service.PositionChanged += (_, _) => positions++;
        using var subscription = _broker.Subscribe<PlaybackChanged>(_ => states++);

        await _service.TickAsync();

        Assert.Equal(1, positions);
        Assert.Equal(0, states);
    }

    [Fact]
    public async Task TickAsync_AnnouncesPlaybackChanged_WhenTheSongRunsOut()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromSeconds(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // Past the end, so the tick concludes the performance rather than interpolating. Seek
        // publishes PlaybackChanged of its own, so the counter starts after it.
        await _service.SeekAsync(TimeSpan.FromSeconds(1));

        var states = 0;
        using var subscription = _broker.Subscribe<PlaybackChanged>(_ => states++);

        await _service.TickAsync();

        Assert.Equal(1, states);
        Assert.Equal(PlaybackState.Stopped, _service.State);
    }

    [Fact]
    public async Task ScreenReconnect_LeavesThePositionClockRunning()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // Off zero, so the reconnect sync sends the seek this waits on.
        await _service.TickAsync();
        _screenServer.ClearReceivedCalls();

        // The reconnect sync stops the clock to reload the screen; the song is still playing, so
        // it has to be running again by the time that finishes or the playhead sticks.
        RaiseScreenConnected();
        Assert.True(await WaitForBroadcastAsync<SeekCommand>());

        var ticks = 0;
        _service.PositionChanged += (_, _) => Interlocked.Increment(ref ticks);

        for (var i = 0; i < 200 && Volatile.Read(ref ticks) == 0; i++)
            await Task.Delay(10);

        Assert.True(ticks > 0, "the clock never ticked again after the screen reconnected");
    }

    [Fact]
    public async Task PositionClock_GoesSilentOnStop_EvenWhenSeeksRacedEachOther()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // Seek stops the clock and starts it again, so concurrent seeks drive two threads through
        // that swap at once. Unsynchronised, one of them assigns a Timer the other has already
        // replaced — orphaned, unreachable, and never disposed by the stop below.
        await Task.WhenAll(Enumerable.Range(0, 64).Select(i =>
            Task.Run(() => _service.SeekAsync(TimeSpan.FromSeconds(i % 5)))));

        await _service.StopAsync();

        // Let any tick already in flight when the clock stopped finish before counting.
        await Task.Delay(150);

        var ticks = 0;
        _service.PositionChanged += (_, _) => Interlocked.Increment(ref ticks);

        // Two clock intervals: an orphan ticking at 500ms cannot hide inside this window.
        await Task.Delay(1200);

        Assert.Equal(0, ticks);
    }

    // The bed yields to the song and comes back after it. Both live here because
    // PlaybackService is what knows a performance started and what knows one finished.
    [Fact]
    public async Task LoadAsync_SuspendsBreakMusic()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);

        await _breakMusic.Received(1).SuspendAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_RefusedMedia_LeavesBreakMusicAlone()
    {
        var (performance, media) = CreatePerformance();
        media.Status = MediaStatus.Broken;

        await _service.LoadAsync(performance, media);

        await _breakMusic.DidNotReceive().SuspendAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_DoesNotRestoreBreakMusic()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);

        await _breakMusic.DidNotReceive().RestoreAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaybackEnding_RestoresBreakMusic()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMilliseconds(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await Task.Delay(20);
        await _service.TickAsync();

        await _breakMusic.Received(1).RestoreAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A still with no voiceover is silent, and starting one deliberately leaves the bed alone —
    /// but the song that just ended had already put it down, so nothing was picking it back up.
    /// The room heard the whole ad in silence.
    /// </summary>
    [Fact]
    public async Task PlaybackEnding_ASilentAdTakesTheGap_BringsTheBedBackUnderIt()
    {
        _service.PerformanceEnded += (_, gap) => gap.Fill(_service.PlayAdAsync(new AdPlayback
        {
            Visual = CreateStillAd(),
            Duration = TimeSpan.FromSeconds(10),
        }));

        await EndAPerformanceAsync();

        await _breakMusic.Received(1).RestoreAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The other half of the rule: nothing plays underneath an ad the room can hear.</summary>
    [Fact]
    public async Task PlaybackEnding_AnAdWithItsOwnAudioTakesTheGap_LeavesTheBedDown()
    {
        _service.PerformanceEnded += (_, gap) => gap.Fill(_service.PlayAdAsync(new AdPlayback
        {
            Visual = CreateAd(),
            Duration = TimeSpan.FromSeconds(10),
        }));

        await EndAPerformanceAsync();

        await _breakMusic.DidNotReceive().RestoreAsync(Arg.Any<CancellationToken>());
    }

    // Rotation is what the next singer is waiting on, so the bed must not come back ahead of it.
    [Fact]
    public async Task PlaybackEnding_RestoresBreakMusicAfterRotating()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMilliseconds(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await Task.Delay(20);
        await _service.TickAsync();

        Received.InOrder(() =>
        {
            _queueService.RotateQueueAsync(performance.SingerId);
            _breakMusic.RestoreAsync(Arg.Any<CancellationToken>());
        });
    }

    private static Media CreateAd(TimeSpan? duration = null) => new()
    {
        Id = Guid.NewGuid(),
        FilePath = "/media/spot.mp4",
        Title = "Happy Hour",
        Status = MediaStatus.Ready,
        Type = MediaType.Video,
        Duration = duration ?? TimeSpan.FromSeconds(20),
    };

    [Fact]
    public async Task PlayAdAsync_PlaysOnTheMainChannel()
    {
        Assert.True(await _service.PlayAdAsync(CreateAd()));

        Assert.Equal(PlaybackState.Playing, _service.State);
        Assert.True(_service.IsPlayingAd);
        await _screenServer.Received().BroadcastCommandAsync(Arg.Any<LoadMediaCommand>());
    }

    // An ad is nobody's turn, so none of the queue machinery a performance triggers may run.
    [Fact]
    public async Task PlayAdAsync_DoesNotTouchTheSingerQueue()
    {
        await _service.PlayAdAsync(CreateAd());

        await _queueService.DidNotReceive().MoveUserToStartAsync(Arg.Any<Guid>());
        _queueService.DidNotReceive().LockTopSlot();
    }

    [Fact]
    public async Task PlayAdAsync_LeavesCurrentPerformanceNull()
    {
        await _service.PlayAdAsync(CreateAd());

        Assert.Null(_service.CurrentPerformance);
        Assert.Null(_service.CurrentlyPerformingUserId);
    }

    [Fact]
    public async Task PlayAdAsync_SuspendsBreakMusic()
    {
        await _service.PlayAdAsync(CreateAd());

        await _breakMusic.Received(1).SuspendAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlayAdAsync_RefusesMediaThatIsNotReady()
    {
        var ad = CreateAd();
        ad.Status = MediaStatus.Broken;

        Assert.False(await _service.PlayAdAsync(ad));
        Assert.False(_service.IsPlayingAd);
        Assert.Equal(PlaybackState.Stopped, _service.State);
    }

    // Nothing watches an ad the way a host watches a song, and the clock ends playback by
    // duration — one without a duration would hold the main channel all night.
    [Fact]
    public async Task PlayAdAsync_RefusesMediaWithNoDuration()
    {
        var ad = CreateAd();
        ad.Duration = null;

        Assert.False(await _service.PlayAdAsync(ad));
        Assert.False(_service.IsPlayingAd);
    }

    [Fact]
    public async Task PlayAdAsync_RefusesMediaWithZeroDuration()
    {
        Assert.False(await _service.PlayAdAsync(CreateAd(TimeSpan.Zero)));
    }

    [Fact]
    public async Task PlayAdAsync_WhileAPerformanceIsLoaded_IsRefused()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        Assert.False(await _service.PlayAdAsync(CreateAd()));

        Assert.Same(performance, _service.CurrentPerformance);
        Assert.False(_service.IsPlayingAd);
    }

    [Fact]
    public async Task PlayAdAsync_WithNoScreens_ClearsItselfRatherThanHoldingTheChannel()
    {
        ConnectScreens(0);

        Assert.False(await _service.PlayAdAsync(CreateAd()));

        Assert.False(_service.IsPlayingAd);
        Assert.Null(_service.CurrentMedia);
        // Otherwise the bed stays suspended behind an ad that never plays or ends.
        await _breakMusic.Received(1).RestoreAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAdEnding_DoesNotDequeueOrRotate()
    {
        await _service.PlayAdAsync(CreateAd(TimeSpan.FromMilliseconds(1)));
        await Task.Delay(20);
        await _service.TickAsync();

        await _performanceService.DidNotReceive().DequeueAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
        await _queueService.DidNotReceive().RotateQueueAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task AnAdEnding_RestoresBreakMusic()
    {
        await _service.PlayAdAsync(CreateAd(TimeSpan.FromMilliseconds(1)));
        await Task.Delay(20);
        await _service.TickAsync();

        await _breakMusic.Received(1).RestoreAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAdEnding_ClearsTheAdFlag()
    {
        await _service.PlayAdAsync(CreateAd(TimeSpan.FromMilliseconds(1)));
        await Task.Delay(20);
        await _service.TickAsync();

        Assert.False(_service.IsPlayingAd);
        Assert.Null(_service.CurrentMedia);
    }

    [Fact]
    public async Task StoppingAnAd_DoesNotRotateTheQueue()
    {
        await _service.PlayAdAsync(CreateAd());

        await _service.StopAsync();

        await _queueService.DidNotReceive().RotateQueueAsync(Arg.Any<Guid>());
        Assert.False(_service.IsPlayingAd);
    }

    // Loading over a running ad is the path that matters: stopping the ad first clears the flag on
    // its own, so a test that stops first passes even when Load never clears it.
    [Fact]
    public async Task LoadingAPerformanceOverARunningAd_ClearsTheAdFlag()
    {
        await _service.PlayAdAsync(CreateAd());
        Assert.True(_service.IsPlayingAd);

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        Assert.False(_service.IsPlayingAd);
    }

    // The consequence of that flag surviving: the singer's own song would end down the ad path and
    // never dequeue them, leaving them stuck at the top of the queue.
    [Fact]
    public async Task APerformanceLoadedOverARunningAd_StillDequeuesWhenItEnds()
    {
        await _service.PlayAdAsync(CreateAd());

        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMilliseconds(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await Task.Delay(20);
        await _service.TickAsync();

        await _performanceService.Received(1).DequeueAsync(performance.SingerId, performance.Id);
        await _queueService.Received(1).RotateQueueAsync(performance.SingerId);
    }

    private static Media CreateStillAd(string format = "PNG") => new()
    {
        Id = Guid.NewGuid(),
        FilePath = "/media/card.png",
        Title = "Happy Hour Card",
        Status = MediaStatus.Ready,
        Type = MediaType.Video,
        Format = format,
        Duration = TimeSpan.FromSeconds(15),
    };

    private void VenueBranding(Guid? mediaId, string format = "PNG")
    {
        _venuesService.ReadSelectedVenueAsync().Returns(new Venue
        {
            Id = Guid.NewGuid(),
            Name = "Test Venue",
            Settings = new Venue.VenueSettings { BrandingImageMediaId = mediaId },
        });

        if (mediaId is { } id)
        {
            _mediaService.ReadAsync(id).Returns(new Media
            {
                Id = id,
                FilePath = "/media/brand.png",
                Title = "Venue Card",
                Status = MediaStatus.Ready,
                Format = format,
            });
        }

        _mediaStreams.BuildImageUrl(Arg.Any<Guid>()).Returns(c => $"http://host/media/image/{c.ArgAt<Guid>(0)}");
    }

    [Fact]
    public async Task PlayAdAsync_AStill_ShowsItWithoutOpeningATranscode()
    {
        _mediaStreams.BuildImageUrl(Arg.Any<Guid>()).Returns("http://host/media/image/x");

        Assert.True(await _service.PlayAdAsync(CreateStillAd()));

        await _screenServer.Received().BroadcastCommandAsync(Arg.Any<ShowImageCommand>());
        await _mediaStreams.DidNotReceive().OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<LoadMediaCommand>());
    }

    // A still has no audio, so the room would sit in silence for its whole duration if the bed
    // yielded to it the way it yields to a video.
    [Fact]
    public async Task PlayAdAsync_AStill_LeavesBreakMusicPlaying()
    {
        _mediaStreams.BuildImageUrl(Arg.Any<Guid>()).Returns("http://host/media/image/x");

        await _service.PlayAdAsync(CreateStillAd());

        await _breakMusic.DidNotReceive().SuspendAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlayAdAsync_AVideoAd_StillSuspendsBreakMusic()
    {
        await _service.PlayAdAsync(CreateAd());

        await _breakMusic.Received(1).SuspendAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlayAdAsync_AStill_RunsOnTheHostClockAndEnds()
    {
        _mediaStreams.BuildImageUrl(Arg.Any<Guid>()).Returns("http://host/media/image/x");

        var still = CreateStillAd();
        still.Duration = TimeSpan.FromMilliseconds(1);

        await _service.PlayAdAsync(still);
        Assert.Equal(PlaybackState.Playing, _service.State);

        await Task.Delay(20);
        await _service.TickAsync();

        Assert.False(_service.IsPlayingAd);
        Assert.Equal(PlaybackState.Stopped, _service.State);
    }

    [Fact]
    public async Task PlayAdAsync_AStillWithNoScreens_IsRefused()
    {
        ConnectScreens(0);

        Assert.False(await _service.PlayAdAsync(CreateStillAd()));

        Assert.False(_service.IsPlayingAd);
        Assert.Null(_service.CurrentMedia);
    }

    [Fact]
    public async Task PlaybackEnding_WithVenueBranding_ShowsTheCard()
    {
        var brandingId = Guid.NewGuid();
        VenueBranding(brandingId);

        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMilliseconds(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await Task.Delay(20);
        await _service.TickAsync();

        await _screenServer.Received().BroadcastCommandAsync(
            Arg.Is<ShowImageCommand>(c => c.Url.Contains(brandingId.ToString())));
    }

    [Fact]
    public async Task PlaybackEnding_WithNoVenueBranding_HidesWhateverWasThere()
    {
        VenueBranding(null);

        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMilliseconds(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // Cleared because LoadAsync hides the card too: without this the assertion is satisfied by
        // that call and passes even when the end of a song leaves the last still on screen.
        _screenServer.ClearReceivedCalls();

        await Task.Delay(20);
        await _service.TickAsync();

        await _screenServer.Received().BroadcastCommandAsync(Arg.Any<HideImageCommand>());
    }

    // A branding row pointing at a song would otherwise be handed to the screen as an image URL
    // that serves nothing.
    [Fact]
    public async Task PlaybackEnding_WithBrandingThatIsNotAnImage_HidesInstead()
    {
        VenueBranding(Guid.NewGuid(), format: "MP4");

        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMilliseconds(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        _screenServer.ClearReceivedCalls();

        await Task.Delay(20);
        await _service.TickAsync();

        await _screenServer.Received().BroadcastCommandAsync(Arg.Any<HideImageCommand>());
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<ShowImageCommand>());
    }

    [Fact]
    public async Task LoadAsync_HidesTheVenueCard()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);

        await _screenServer.Received().BroadcastCommandAsync(Arg.Any<HideImageCommand>());
    }

    private async Task EndAPerformanceAsync()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMilliseconds(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        _screenServer.ClearReceivedCalls();
        _breakMusic.ClearReceivedCalls();

        await Task.Delay(20);
        await _service.TickAsync();
    }

    [Fact]
    public async Task PlaybackEnding_RaisesPerformanceEnded()
    {
        var raised = 0;
        _service.PerformanceEnded += (_, _) => raised++;

        await EndAPerformanceAsync();

        Assert.Equal(1, raised);
    }

    // An ad is nobody's turn. Raising it here would count the ad towards the next ad, and could
    // chain them without a singer getting back on.
    [Fact]
    public async Task AnAdEnding_DoesNotRaisePerformanceEnded()
    {
        await _service.PlayAdAsync(CreateAd(TimeSpan.FromMilliseconds(1)));

        var raised = 0;
        _service.PerformanceEnded += (_, _) => raised++;

        await Task.Delay(20);
        await _service.TickAsync();

        Assert.Equal(0, raised);
    }

    // The whole reason the gap carries work rather than being a plain void event: a handler that
    // starts an ad must finish starting it before the bed is brought back underneath it.
    [Fact]
    public async Task PlaybackEnding_AwaitsWorkRegisteredOnTheGap()
    {
        var finished = false;

        _service.PerformanceEnded += (_, gap) => gap.Fill(Task.Run(async () =>
        {
            await Task.Delay(30);
            finished = true;
        }));

        await EndAPerformanceAsync();

        Assert.True(finished);
    }

    [Fact]
    public async Task PlaybackEnding_WhenTheGapStartedAnAd_LeavesBreakMusicDown()
    {
        var ad = CreateAd();

        _service.PerformanceEnded += (_, gap) => gap.Fill(_service.PlayAdAsync(ad));

        await EndAPerformanceAsync();

        Assert.True(_service.IsPlayingAd);
        await _breakMusic.DidNotReceive().RestoreAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaybackEnding_WhenNothingTookTheGap_RestoresBreakMusic()
    {
        _service.PerformanceEnded += (_, _) => { };

        await EndAPerformanceAsync();

        await _breakMusic.Received(1).RestoreAsync(Arg.Any<CancellationToken>());
    }

    private static Media CreateAudio(TimeSpan? duration = null) => new()
    {
        Id = Guid.NewGuid(),
        FilePath = "/media/voiceover.mp3",
        Title = "Voiceover",
        Status = MediaStatus.Ready,
        Type = MediaType.Video,
        Format = "MP3",
        Duration = duration ?? TimeSpan.FromSeconds(12),
    };

    // A still with a voiceover: the picture is on the main channel, the words on the bed's channel,
    // and the bed itself has to get out of the way because the room now hears the ad.
    [Fact]
    public async Task PlayAdAsync_AStillWithItsOwnAudio_UsesBothChannels()
    {
        _mediaStreams.BuildImageUrl(Arg.Any<Guid>()).Returns("http://host/media/image/x");

        var ad = new AdPlayback
        {
            Visual = CreateStillAd(),
            Audio = CreateAudio(),
            Duration = TimeSpan.FromSeconds(12),
        };

        Assert.True(await _service.PlayAdAsync(ad));

        await _screenServer.Received().BroadcastCommandAsync(Arg.Any<ShowImageCommand>());
        await _screenServer.Received().SendCommandAsync(Arg.Any<string>(), Arg.Any<LoadBackgroundCommand>());
        await _breakMusic.Received(1).SuspendAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlayAdAsync_AudioOnly_LeavesTheScreenAlone()
    {
        var ad = new AdPlayback { Audio = CreateAudio(), Duration = TimeSpan.FromSeconds(12) };

        Assert.True(await _service.PlayAdAsync(ad));

        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<ShowImageCommand>());
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<LoadMediaCommand>());
        await _screenServer.Received().SendCommandAsync(Arg.Any<string>(), Arg.Any<LoadBackgroundCommand>());
    }

    // The whole point of a segment: a clip out of a longer file costs no re-encode, because the
    // stream is simply opened at the offset.
    [Fact]
    public async Task PlayAdAsync_AnAudioSegment_OpensTheStreamAtTheOffset()
    {
        var ad = new AdPlayback
        {
            Audio = CreateAudio(TimeSpan.FromMinutes(5)),
            AudioStart = TimeSpan.FromSeconds(90),
            Duration = TimeSpan.FromSeconds(20),
        };

        await _service.PlayAdAsync(ad);

        await _mediaStreams.Received().OpenAsync("/media/voiceover.mp3", TimeSpan.FromSeconds(90),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlayAdAsync_RunsForTheCompositionsDurationNotTheFiles()
    {
        var ad = new AdPlayback
        {
            Audio = CreateAudio(TimeSpan.FromMinutes(5)),
            Duration = TimeSpan.FromMilliseconds(1),
        };

        await _service.PlayAdAsync(ad);
        await Task.Delay(20);
        await _service.TickAsync();

        Assert.False(_service.IsPlayingAd);
    }

    [Fact]
    public async Task AnAdWithItsOwnAudioEnding_HandsTheChannelBack()
    {
        var ad = new AdPlayback { Audio = CreateAudio(), Duration = TimeSpan.FromMilliseconds(1) };

        await _service.PlayAdAsync(ad);
        _screenServer.ClearReceivedCalls();

        await Task.Delay(20);
        await _service.TickAsync();

        // Stopped before break music reclaims the channel, or the bed would come up over a
        // voiceover that is still playing on it.
        await _screenServer.Received().SendCommandAsync(Arg.Any<string>(), Arg.Any<StopBackgroundCommand>());
        await _mediaStreams.Received().CloseAsync(Arg.Any<string>());
    }

    // A host who needs the next singer up cannot be held by a fifteen-second card, so a load cuts
    // whatever ad is running rather than being refused behind it.
    [Fact]
    public async Task LoadAsync_WhileAnAdIsPlaying_CutsTheAdShort()
    {
        await _service.PlayAdAsync(CreateAd());
        Assert.True(_service.IsPlayingAd);

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        Assert.False(_service.IsPlayingAd);
        Assert.Same(performance, _service.CurrentPerformance);
        Assert.Same(media, _service.CurrentMedia);
    }

    // The main channel is reloaded with the song either way; the ad's own audio is on the other
    // channel, and only this hands it back — otherwise a voiceover plays under the singer.
    [Fact]
    public async Task LoadAsync_WhileAnAdWithItsOwnAudioIsPlaying_HandsTheChannelBack()
    {
        await _service.PlayAdAsync(new AdPlayback { Audio = CreateAudio(), Duration = TimeSpan.FromSeconds(12) });
        _screenServer.ClearReceivedCalls();
        _mediaStreams.ClearReceivedCalls();

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        await _screenServer.Received().SendCommandAsync(Arg.Any<string>(), Arg.Any<StopBackgroundCommand>());
        await _mediaStreams.Received().CloseAsync(Arg.Any<string>());
    }

    // Cutting an ad is not the same as one ending: what follows here is a song, so the bed and the
    // venue card that trail a finished ad must not come up over it.
    [Fact]
    public async Task LoadAsync_WhileAnAdIsPlaying_DoesNotBringBreakMusicBack()
    {
        await _service.PlayAdAsync(CreateAd());
        _breakMusic.ClearReceivedCalls();

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        await _breakMusic.DidNotReceive().RestoreAsync(Arg.Any<CancellationToken>());
    }

    // The clock ran the ad, and a stale duration would end the song at the ad's length instead.
    [Fact]
    public async Task LoadAsync_WhileAnAdIsPlaying_RunsTheSongForItsOwnLength()
    {
        await _service.PlayAdAsync(CreateAd(TimeSpan.FromMilliseconds(1)));

        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMinutes(4);
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await Task.Delay(20);
        await _service.TickAsync();

        Assert.Equal(PlaybackState.Playing, _service.State);
        Assert.Same(performance, _service.CurrentPerformance);
    }

    [Fact]
    public async Task PlayAdAsync_WithNothingToShowOrPlay_IsRefused()
    {
        Assert.False(await _service.PlayAdAsync(new AdPlayback { Duration = TimeSpan.FromSeconds(5) }));
    }

    [Fact]
    public async Task PlayAdAsync_WithBrokenAudio_IsRefused()
    {
        var audio = CreateAudio();
        audio.Status = MediaStatus.Broken;

        Assert.False(await _service.PlayAdAsync(new AdPlayback
        {
            Visual = CreateStillAd(),
            Audio = audio,
            Duration = TimeSpan.FromSeconds(5),
        }));

        Assert.False(_service.IsPlayingAd);
    }

    // A screen joining an idle host used to get nothing at all, so it sat on the bare "KHost"
    // placeholder while every other screen showed the venue's card.
    [Fact]
    public async Task ScreenConnecting_WhileIdle_ShowsTheVenueCard()
    {
        var brandingId = Guid.NewGuid();
        VenueBranding(brandingId);
        _screenServer.ClearReceivedCalls();

        RaiseScreenConnected();

        Assert.True(await WaitForBroadcastAsync<ShowImageCommand>());
    }

    [Fact]
    public async Task ScreenConnecting_WhileIdleWithNoBranding_ClearsTheScreen()
    {
        VenueBranding(null);
        _screenServer.ClearReceivedCalls();

        RaiseScreenConnected();

        Assert.True(await WaitForBroadcastAsync<HideImageCommand>());
    }

    // A still is on the screen, not in a stream. Reloading it would try to open an ffmpeg
    // transcode for a picture, which fails and leaves the joiner showing nothing.
    [Fact]
    public async Task ScreenConnecting_WhileAStillIsUp_ReshowsItWithoutOpeningATranscode()
    {
        _mediaStreams.BuildImageUrl(Arg.Any<Guid>()).Returns("http://host/media/image/x");

        await _service.PlayAdAsync(CreateStillAd());
        _screenServer.ClearReceivedCalls();
        _mediaStreams.ClearReceivedCalls();

        RaiseScreenConnected();

        Assert.True(await WaitForBroadcastAsync<ShowImageCommand>());
        await _mediaStreams.DidNotReceive().OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScreenConnecting_WhileASongIsLoaded_StillReloadsIt()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        _screenServer.ClearReceivedCalls();

        RaiseScreenConnected();

        Assert.True(await WaitForBroadcastAsync<LoadMediaCommand>());
    }

    // The screen holds no library, so the host's choice has to travel with the picture.
    [Fact]
    public async Task PlayAdAsync_AStill_SendsItsScalingWithTheImage()
    {
        _mediaStreams.BuildImageUrl(Arg.Any<Guid>()).Returns("http://host/media/image/x");

        var still = CreateStillAd();
        still.ImageScaling = ImageScaling.Fill;

        await _service.PlayAdAsync(still);

        await _screenServer.Received().BroadcastCommandAsync(
            Arg.Is<ShowImageCommand>(c => c.Scaling == ImageScaling.Fill));
    }

    [Fact]
    public async Task TheVenueCard_IsShownWithItsOwnScaling()
    {
        var brandingId = Guid.NewGuid();
        VenueBranding(brandingId);
        _mediaService.ReadAsync(brandingId).Returns(new Media
        {
            Id = brandingId,
            FilePath = "/media/brand.png",
            Title = "Venue Card",
            Status = MediaStatus.Ready,
            Format = "PNG",
            ImageScaling = ImageScaling.Stretch,
        });

        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMilliseconds(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await Task.Delay(20);
        await _service.TickAsync();

        await _screenServer.Received().BroadcastCommandAsync(
            Arg.Is<ShowImageCommand>(c => c.Scaling == ImageScaling.Stretch));
    }

    private static (Performance, Media) CreatePerformance()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var performance = new Performance
        {
            Id = Guid.NewGuid(),
            SingerId = singerId,
            MediaId = mediaId,
            CreatedDate = DateTime.Now,
            QueuePosition = 1
        };
        var media = new Media { Id = mediaId, FilePath = "/music/media.mp4", Title = "Media", Status = MediaStatus.Ready };
        return (performance, media);
    }
}
