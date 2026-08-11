using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

public class PlaybackServiceTests : IDisposable
{
    private readonly ILogger<PlaybackService> _logger = Substitute.For<ILogger<PlaybackService>>();
    private readonly ISingerQueueService _queueService = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performanceService = Substitute.For<IPerformanceService>();
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly PlaybackService _service;

    public PlaybackServiceTests()
    {
        _venuesService.ReadSelectedVenueAsync()
            .Returns(new Venue { Id = Guid.NewGuid(), Name = "Test Venue", Settings = new Venue.VenueSettings() });

        // Playback refuses to start with no screen attached, so the default fixture has one.
        ConnectScreens(1);

        // Zero fade keeps stop synchronous; the fading behaviour has its own tests below.
        _service = MakeService(TimeSpan.Zero);
    }

    private void ConnectScreens(int count)
    {
        var screens = Enumerable.Range(1, count).Select(i =>
        {
            var screen = Substitute.For<IScreenConnection>();
            screen.ScreenId.Returns($"Screen {i}");
            screen.ConnectionId.Returns($"conn-{i}");
            screen.IsConnected.Returns(true);
            return screen;
        }).ToArray();

        _screenServer.GetConnectedScreensAsync().Returns(_ => ToAsyncEnumerable(screens));
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
    public async Task LoadAsync_BroadcastsLoadMediaCommand_WithMediaFilePath()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);

        await _screenServer.Received(1).BroadcastCommandAsync(
            Arg.Is<LoadMediaCommand>(c => c.FilePath == media.FilePath));
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
