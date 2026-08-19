using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

public class PlaybackServiceTests : IDisposable
{
    private readonly ILogger<PlaybackService> _logger = Substitute.For<ILogger<PlaybackService>>();
    private readonly ISingerQueueService _queueService = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performanceService = Substitute.For<IPerformanceService>();
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly IMediaStreamService _mediaStreams = Substitute.For<IMediaStreamService>();
    private readonly ICastService _cast = Substitute.For<ICastService>();

    // Real: a substitute would make the IsPrimary assertions below test nothing.
    private readonly ScreenCoordinationService _screenCoordination;
    private readonly PlaybackService _service;
    private int _streamsOpened;

    public PlaybackServiceTests()
    {
        // NSubstitute returns string.Empty for unstubbed strings, so "no receiver" must be said.
        _cast.ConnectedDeviceId.Returns((string?)null);

        _screenCoordination = new ScreenCoordinationService(NullLogger<ScreenCoordinationService>.Instance, _screenServer);

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
            screen.Capabilities.Returns(new ScreenCapabilities { SupportsSync = supportsSync });
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
        Options.Create(new PlaybackService.ServiceOptions { StopFadeDuration = stopFadeDuration }));

    public void Dispose() => _service.Dispose();

    [Fact]
    public void NewService_StartsStopped()
    {
        Assert.Equal(PlaybackState.Stopped, _service.State);
        Assert.Null(_service.CurrentPerformance);
        Assert.Equal(TimeSpan.Zero, _service.Position);
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
    public async Task Load_RaisesStateChanged()
    {
        var raised = false;
        _service.StateChanged += (_, _) => raised = true;

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        Assert.True(raised);
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
                LoadedFilePath = "/library/song.mp4",
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
    public async Task StopAsync_RaisesStateChanged_WhenEnteringStopping()
    {
        var service = MakeService(TimeSpan.FromMilliseconds(200));
        var (performance, media) = CreatePerformance();

        await service.LoadAsync(performance, media);
        await service.PlayAsync();

        var changes = 0;
        service.StateChanged += (_, _) => changes++;

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

        await Task.Delay(750);

        Assert.Equal(PlaybackState.Stopped, _service.State);
        await _performanceService.Received().DequeueAsync(performance.SingerId, performance.Id);
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
        var media = new Media { Id = mediaId, FilePath = "/music/media.mp4", Title = "Media" };
        return (performance, media);
    }
}
