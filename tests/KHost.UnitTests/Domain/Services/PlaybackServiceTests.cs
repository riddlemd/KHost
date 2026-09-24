using KHost.Abstractions.Messaging;
using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using KHost.Domain.Services;
using KHost.Domain.Services.Screens;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging.Messages;

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
    private readonly IDisplayProvider _display = Substitute.For<IDisplayProvider>();
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IAudioTrackService _audioTracks = Substitute.For<IAudioTrackService>();
    private readonly IMediaGateService _mediaGate = Substitute.For<IMediaGateService>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();
    private readonly ITimedLyricsService _timedLyrics = Substitute.For<ITimedLyricsService>();

    private readonly StubRenderer _renderer = new();
    private readonly PlaybackService _service;
    private int _streamsOpened;

    public PlaybackServiceTests()
    {
        // NSubstitute returns string.Empty for unstubbed strings, so "no receiver" must be said.
        _display.ConnectedDeviceId.Returns((string?)null);

        // Nothing is gated by default; a Task wrapping null here would NRE the load's gate check.
        _mediaGate.EvaluateAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>()).Returns(PlaybackGateResult.Ok);

        _venuesService.ReadSelectedVenueAsync()
            .Returns(new Venue { Id = Guid.NewGuid(), Name = "Test Venue", Settings = new Venue.VenueSettings() });

        // Playback refuses to start with no screen attached, so the default fixture has one.
        ConnectScreens(1);

        _mediaStreams
            .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>())
            .Returns(call => new MediaStreamSession
            {
                Id = $"stream-{Interlocked.Increment(ref _streamsOpened)}",
                SourcePath = call.ArgAt<string>(0),
                PlaylistUrl = $"http://host/media/stream-{_streamsOpened}/stream.m3u8",
                StartOffset = call.ArgAt<TimeSpan>(1),
                Pitch = call.ArgAt<int>(2),
                Tempo = call.ArgAt<int>(3),
            });

        // Zero fade keeps stop synchronous; the fading behaviour has its own tests below.
        _service = MakeService(TimeSpan.Zero);
    }

    /// <summary>The screens as the display they now are, over the same substituted server, so
    /// ConnectScreens still decides whether the song has anywhere to come out.</summary>
    /// <remarks>The provider tracks connections from the server's <em>events</em> rather than
    /// reading them back — a read back deadlocks a Blazor render — so a screen the fixture
    /// connected before this existed has to be replayed into it. This runs while PlaybackService's
    /// own constructor arguments are still being evaluated, so nothing else is subscribed yet.</remarks>
    /// <remarks>It draws the picture from the program of the service it is built for, found
    /// through <paramref name="services"/> once that service exists, as the container does.</remarks>
    private ScreenDisplayProvider ScreensAsADisplay(IServiceProvider services)
    {
        var provider = new ScreenDisplayProvider(
            NullLogger<ScreenDisplayProvider>.Instance,
            _screenServer,
            [],
            _broker,
            _venuesService,
            services: services);

        foreach (var screen in _connectedScreens)
            _screenServer.ScreenConnected += Raise.EventWith(
                _screenServer, new ScreenConnectionEventArgs { Connection = screen });

        return provider;
    }

    private IScreenConnection[] _connectedScreens = [];

    private void ConnectScreens(int count)
    {
        var screens = Enumerable.Range(1, count).Select(i =>
        {
            var screen = Substitute.For<IScreenConnection>();
            screen.ScreenId.Returns($"Screen {i}");
            screen.ConnectionId.Returns($"conn-{i}");
            screen.IsConnected.Returns(true);
            // A Photino screen declares both: it carries the room and draws the picture.
            screen.Capabilities.Returns(new ScreenCapabilities
            {
                SupportsAudio = true,
                SupportsVideo = true,
            });
            return screen;
        }).ToArray();

        var previous = _connectedScreens;
        _connectedScreens = screens;
        _screenServer.GetConnectedScreensAsync().Returns(_ => ToAsyncEnumerable(screens));

        // Raise what the real server would, so a provider tracking its events ends up agreeing
        // with what this stub reports. Only the delta: re-announcing a screen that never left
        // would have the host sync it again and throw off what the test counted.
        foreach (var gone in previous.Where(p => !screens.Any(s => s.ConnectionId == p.ConnectionId)))
            _screenServer.ScreenDisconnected += Raise.EventWith(
                _screenServer, new ScreenConnectionEventArgs { Connection = gone });

        foreach (var arrived in screens.Where(s => !previous.Any(p => p.ConnectionId == s.ConnectionId)))
            _screenServer.ScreenConnected += Raise.EventWith(
                _screenServer, new ScreenConnectionEventArgs { Connection = arrived });
    }

    private static async IAsyncEnumerable<IScreenConnection> ToAsyncEnumerable(IScreenConnection[] screens)
    {
        foreach (var screen in screens)
            yield return screen;

        await Task.CompletedTask;
    }

    private PlaybackService MakeService(
        TimeSpan stopFadeDuration,
        TimeSpan? pitchSettleDelay = null,
        int defaultBackingVolume = AudioMix.DefaultBackingVolume,
        TimeSpan? retireGrace = null)
    {
        PlaybackService? built = null;

        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IPlaybackProgram)).Returns(_ => built);
        services.GetService(typeof(IMediaService)).Returns(_mediaService);
        services.GetService(typeof(IMediaStreamService)).Returns(_mediaStreams);

        return built = new(
        _logger,
        _queueService,
        _performanceService,
        _venuesService,
        Substitute.For<IAnalyticsService>(),
        _screenServer,
        _mediaStreams,
        // The real router over the real fallback, so these tests still arrange the stream service
        // they always did and the renderer layer is exercised rather than stubbed past.
        new MediaRendererService(
            NullLogger<MediaRendererService>.Instance,
            [_renderer],
            new StreamingMediaRenderer(_mediaStreams)),
        [ScreensAsADisplay(services), _display],
        _breakMusic,
        Monitor(new PlaybackService.ServiceOptions
        {
            StopFadeDuration = stopFadeDuration,
            // The delay exists to collapse a burst of presses; the collapsing has its own test.
            PitchSettleDelay = pitchSettleDelay ?? TimeSpan.Zero,
            DefaultBackingVolume = defaultBackingVolume,
            // The grace exists so a consumer can finish reading the old session; the tests assert
            // on what was closed, not on when.
            StreamRetireGrace = retireGrace ?? TimeSpan.Zero,
        }),
        _audioTracks,
        _mediaGate,
        _timedLyrics,
        _flash,
        _broker);
    }

    /// <summary>The service reads options per use, so a test's values have to answer every read.</summary>
    private static IOptionsMonitor<T> Monitor<T>(T value) where T : class
    {
        var monitor = Substitute.For<IOptionsMonitor<T>>();
        monitor.CurrentValue.Returns(value);
        return monitor;
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void NewService_StartsStopped()
    {
        Assert.Equal(PlaybackState.Stopped, _service.State);
        Assert.Null(_service.CurrentPerformance);
        Assert.Equal(TimeSpan.Zero, _service.Position);
    }

    /// <summary>A load that throws must restore state, or the console wedges with disabled buttons.</summary>
    private void FailTheStreamOpen() => _mediaStreams
        .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>())
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

        // Named before the failing step, so the clear has something to clear rather than passing
        // on a name that was never resolved.
        ArrangeSinger(performance.SingerId, "Priya");

        FailTheStreamOpen();

        await Assert.ThrowsAsync<KHostException>(() => _service.LoadAsync(performance, media));

        // Left set, this disables the row's own remove button and every row's play button.
        Assert.Null(_service.CurrentPerformance);
        Assert.Null(_service.CurrentMedia);
        Assert.Null(_service.CurrentSingerName);
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
            .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>())
            .Returns(call => new MediaStreamSession
            {
                Id = "stream-recovered",
                SourcePath = call.ArgAt<string>(0),
                PlaylistUrl = "http://host/media/stream-recovered/stream.m3u8",
                StartOffset = call.ArgAt<TimeSpan>(1),
                Pitch = call.ArgAt<int>(2),
                Tempo = call.ArgAt<int>(3),
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
    public async Task LoadAsync_GateBlocksTheMedia_RefusesAndFlashesTheReason()
    {
        var (performance, media) = CreatePerformance();
        _mediaGate.EvaluateAsync(Arg.Any<MediaAction>(), media, Arg.Any<CancellationToken>())
            .Returns(new PlaybackGateResult(false, "Sign in to the provider to play this track."));

        await _service.LoadAsync(performance, media);

        Assert.Null(_service.CurrentPerformance);
        Assert.Null(_service.CurrentMedia);
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<LoadMediaCommand>());
        _flash.Received(1).Show("Sign in to the provider to play this track.", FlashType.Warning);
    }

    /// <summary>The moment decides the answer, so a load must ask about the play. Asked as a queue
    /// instead, a gate that prompts on enqueue would open a sign-in dialog over a room mid-show.
    /// </summary>
    [Fact]
    public async Task LoadAsync_AsksTheGateAboutPlaying()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);

        await _mediaGate.Received().EvaluateAsync(MediaAction.Play, media, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_GateAllowsTheMedia_Loads()
    {
        var (performance, media) = CreatePerformance();
        _mediaGate.EvaluateAsync(Arg.Any<MediaAction>(), media, Arg.Any<CancellationToken>()).Returns(PlaybackGateResult.Ok);

        await _service.LoadAsync(performance, media);

        Assert.Same(performance, _service.CurrentPerformance);
        _flash.DidNotReceive().Show(Arg.Any<string>(), Arg.Any<FlashType>());
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
    public async Task LoadAsync_ASongWithWords_SendsThemToTheScreens()
    {
        var (performance, media) = CreatePerformance();
        var lyrics = new TimedLyrics { DurationSeconds = 90, Bounds = new LyricBox(0, 0, 640, 360) };
        _timedLyrics.GetTimedLyricsAsync(media.FilePath, Arg.Any<CancellationToken>()).Returns(lyrics);

        await _service.LoadAsync(performance, media);

        await _screenServer.Received(1).BroadcastCommandAsync(
            Arg.Is<SetTimedLyricsCommand>(command => ReferenceEquals(command.Lyrics, lyrics)));
    }

    [Fact]
    public async Task LoadAsync_ASongWithNoWords_StillSendsSoTheLastSongsAreCleared()
    {
        var (performance, media) = CreatePerformance();
        _timedLyrics.GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TimedLyrics?)null);

        await _service.LoadAsync(performance, media);

        // Skipping the send leaves the previous song's words lit over this one.
        await _screenServer.Received(1).BroadcastCommandAsync(
            Arg.Is<SetTimedLyricsCommand>(command => command.Lyrics == null));
    }

    [Fact]
    public async Task LoadAsync_TheLyricsCannotBeRead_LoadsTheSongAnyway()
    {
        var (performance, media) = CreatePerformance();
        _timedLyrics.GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<TimedLyrics?>>(_ => throw new InvalidDataException("bad timing"));

        await _service.LoadAsync(performance, media);

        // A plugin that cannot read its own file costs the words, never the song.
        Assert.Equal(media.Id, _service.CurrentMedia?.Id);
        await _screenServer.Received(1).BroadcastCommandAsync(Arg.Any<LoadMediaCommand>());
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

    /// <summary>Nothing enumerates the hub to answer this any more — the screens report their own
    /// connection from a field, precisely so a Blazor render cannot block on the hub's lock. What
    /// is left to survive is a provider that throws when asked.</summary>
    [Fact]
    public async Task HasConnectedScreenAsync_IsFalse_WhenAProviderThrows()
    {
        ConnectScreens(0);
        _display.ConnectedDeviceId.Returns(_ => throw new InvalidOperationException("transport down"));

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
    // Two screens can no longer both register, so the question this used to ask is now asked
    // across providers: the room still has the song if a television is carrying it.
    public async Task ScreenDisconnect_KeepsPlaying_WhenADisplayStillCarriesTheSong()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        _display.ConnectedDeviceId.Returns("Living Room TV");
        ConnectScreens(0);
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
    public async Task ScreenReconnect_ASongWithWords_SendsThemToTheJoiningScreen()
    {
        var (performance, media) = CreatePerformance();
        var lyrics = new TimedLyrics { DurationSeconds = 90, Bounds = new LyricBox(0, 0, 640, 360) };
        _timedLyrics.GetTimedLyricsAsync(media.FilePath, Arg.Any<CancellationToken>()).Returns(lyrics);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        _screenServer.ClearReceivedCalls();

        RaiseScreenConnected();

        // The words are sent once, when the song starts, to whoever is connected then. Without
        // this a screen that joins mid-song plays the audio and draws nothing.
        Assert.True(await WaitForBroadcastAsync<SetTimedLyricsCommand>());
    }

    [Fact]
    public async Task ScreenReconnect_AfterTheSongEnded_SendsNoStaleWords()
    {
        var (performance, media) = CreatePerformance();
        var lyrics = new TimedLyrics { DurationSeconds = 90, Bounds = new LyricBox(0, 0, 640, 360) };
        _timedLyrics.GetTimedLyricsAsync(media.FilePath, Arg.Any<CancellationToken>()).Returns(lyrics);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.StopAsync();
        _screenServer.ClearReceivedCalls();

        RaiseScreenConnected();

        // Nothing is loaded, so the joiner gets the venue's card. Holding the last song's words
        // would light them over it.
        Assert.False(await WaitForBroadcastAsync<SetTimedLyricsCommand>());
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

    /// <summary>A host pause landing between the sync's awaited display calls must win: replaying
    /// PlayCommand afterwards would restart the song over the host's own pause.</summary>
    [Fact]
    public async Task SyncNewScreenAsync_HostPausesWhileTheSyncIsInFlight_DoesNotReplay()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.TickAsync();
        _screenServer.ClearReceivedCalls();

        // Blocks the sync mid-flight, after the reload and before the trailing play/pause decision,
        // without blocking PauseAsync's own PauseCommand, which is not gated on this.
        var gate = new TaskCompletionSource();
        _screenServer.BroadcastCommandAsync(Arg.Any<IScreenCommand>()).Returns(async call =>
        {
            if (call.Arg<IScreenCommand>() is SetTimedLyricsCommand)
                await gate.Task;
        });

        RaiseScreenConnected();

        // The sync must actually be parked on the gate before the host acts, or the pause below
        // would race a sync that had not started yet.
        Assert.True(await WaitForBroadcastAsync<SetTimedLyricsCommand>());

        await _service.PauseAsync();
        Assert.Equal(PlaybackState.Paused, _service.State);

        gate.SetResult();

        // Give the freed sync a chance to finish running past the point it would have replayed.
        await WaitForAsync(() => _screenServer.ReceivedCalls().Any(c =>
            c.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync) &&
            c.GetArguments().FirstOrDefault() is SeekCommand));

        Assert.Equal(PlaybackState.Paused, _service.State);
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<PlayCommand>());
    }

    /// <summary>A screen joining while the song's first transcode is still starting must not start
    /// another: the load in flight reaches it anyway, and a second one is never closed.</summary>
    [Fact]
    public async Task ScreenConnect_WhileTheSongIsStillRendering_OpensNoSecondStreamAndSendsNoEmptyLoad()
    {
        var (performance, media) = CreatePerformance();
        var opens = 0;
        var rendering = new TaskCompletionSource();
        _mediaStreams
            .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var n = Interlocked.Increment(ref opens);
                await rendering.Task;
                return new MediaStreamSession
                {
                    Id = $"stream-{n}",
                    SourcePath = call.ArgAt<string>(0),
                    PlaylistUrl = $"http://host/media/stream-{n}/stream.m3u8",
                    StartOffset = call.ArgAt<TimeSpan>(1),
                    Pitch = call.ArgAt<int>(2),
                    Tempo = call.ArgAt<int>(3),
                };
            });

        var loading = _service.LoadAsync(performance, media);
        await WaitForAsync(() => Volatile.Read(ref opens) == 1);
        _screenServer.ClearReceivedCalls();

        RaiseScreenConnected();

        // Waited out: the assertion is that the sync does nothing, which has no state to wait for.
        await WaitForAsync(() => Volatile.Read(ref opens) > 1 || _screenServer.ReceivedCalls().Any(c =>
            c.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync) &&
            c.GetArguments().FirstOrDefault() is LoadMediaCommand));

        Assert.Equal(1, Volatile.Read(ref opens));
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<LoadMediaCommand>());

        rendering.SetResult();
        await loading;
    }

    [Fact]
    public async Task ScreenStateReports_MoveTheHostsPosition()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        RaiseScreenState(TimeSpan.FromSeconds(40));
        RaiseScreenState(TimeSpan.FromSeconds(41));

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
        _display.ConnectedDeviceId.Returns("Living Room TV");

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // A television and nothing else attached is the setup a display provider is for.
        Assert.Equal(PlaybackState.Playing, _service.State);
    }

    [Fact]
    public async Task Play_IsStillRefused_WithNoScreenAndNoDevice()
    {
        ConnectScreens(0);
        _display.ConnectedDeviceId.Returns((string?)null);

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // Nothing is playing the song, so the clock must not run the singer's turn away.
        Assert.Equal(PlaybackState.Stopped, _service.State);
    }

    [Fact]
    public async Task Position_FollowsTheReceiver_WhenNoScreenIsUp()
    {
        ConnectScreens(0);
        _display.ConnectedDeviceId.Returns("Living Room TV");

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // The receiver is seconds behind the host's own clock; the host has to take its word.
        _display.PlaybackStatusChanged += Raise.Event<EventHandler<DisplayPlaybackStatus>>(_display,
            new DisplayPlaybackStatus
            {
                Position = TimeSpan.FromSeconds(12),
                IsPlaying = true,
                SampledAtUtc = DateTime.UtcNow,
            });

        Assert.InRange(_service.Position, TimeSpan.FromSeconds(11.5), TimeSpan.FromSeconds(12.5));
    }

    [Fact]
    public async Task Position_IgnoresTheReceiver_WhenAScreenIsPresent()
    {
        ConnectScreens(1);
        _display.ConnectedDeviceId.Returns("Living Room TV");

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        _display.PlaybackStatusChanged += Raise.Event<EventHandler<DisplayPlaybackStatus>>(_display,
            new DisplayPlaybackStatus
            {
                Position = TimeSpan.FromMinutes(3),
                IsPlaying = true,
                SampledAtUtc = DateTime.UtcNow,
            });

        // A screen's reports are timestamped against a measured clock offset; a receiver's are
        // only timestamped on arrival, so the better clock wins.
        Assert.True(_service.Position < TimeSpan.FromSeconds(5), $"position jumped to {_service.Position}");
    }

    [Fact]
    public async Task Playback_DrivesAConnectedCastReceiver()
    {
        ConnectScreens(0);
        _display.ConnectedDeviceId.Returns("Living Room TV");

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // A receiver is not a screen, so nothing broadcasts to it; playback has to drive it.
        // Asserted on the command rather than the bare URL: a provider that cannot mix reaches its
        // own LoadAsync through the default body, which a substitute does not run.
        await _display.Received(1).LoadAsync(
            Arg.Is<LoadMediaCommand>(c =>
                c.StreamUrl == "http://host/media/stream-1/stream.m3u8"
                && c.StreamStartOffset == TimeSpan.Zero
                && c.Tempo == 0),
            Arg.Any<CancellationToken>());
        await _display.Received(1).PlayAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Playback_TouchesNothing_WhenNoCastReceiverIsConnected()
    {
        _display.ConnectedDeviceId.Returns((string?)null);

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.PauseAsync();

        await _display.DidNotReceive().LoadAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _display.DidNotReceive().PlayAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Playback_SurvivesAReceiverThatRefuses()
    {
        ConnectScreens(0);
        _display.ConnectedDeviceId.Returns("Living Room TV");
        _display.PlayAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("receiver went away"));

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // A television switched off mid-song must not take the performance down with it.
        Assert.Equal(PlaybackState.Playing, _service.State);
    }

    [Fact]
    public async Task ACastSessionAppearingMidSong_IsCaughtUpToWhereTheRoomIs()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        _display.ClearReceivedCalls();

        // Selected after the song started: a receiver holds no timeline and hears nothing about
        // a load it was not connected for.
        ConnectCast();

        Assert.True(await WaitForCastLoadAsync());
        await _display.Received(1).PlayAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACastSessionAppearingMidSong_IsNotStarted_WhenTheSongIsPaused()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.PauseAsync();
        _display.ClearReceivedCalls();

        ConnectCast();

        // Loaded so the picture is there, but starting it would have the television playing on
        // its own while the room is parked.
        Assert.True(await WaitForCastLoadAsync());
        await _display.DidNotReceive().PlayAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACastSessionAppearingMidSong_IsMovedToTheCurrentPosition()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.SeekAsync(TimeSpan.FromSeconds(45));
        _display.ClearReceivedCalls();

        ConnectCast();

        // Loading alone starts the stream from its own zero, which is a receiver forty-five
        // seconds behind the room.
        Assert.True(await WaitForCastLoadAsync());
        await _display.Received().SeekAsync(TimeSpan.FromSeconds(45), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARestartedCastReceiver_IsPutBackOnTheSong()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        ConnectCast();
        Assert.True(await WaitForCastLoadAsync());
        _display.ClearReceivedCalls();

        // Same device, new session: what a receiver that restarted looks like. It has forgotten
        // the song, so being told nothing would leave a black television and a playing room.
        _display.SessionId.Returns(Guid.NewGuid());
        await _broker.PublishAsync(new DisplaysChanged());

        Assert.True(await WaitForCastLoadAsync());
    }

    [Fact]
    public async Task ACastSessionThatHasNotChanged_IsLeftAlone()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        ConnectCast();
        Assert.True(await WaitForCastLoadAsync());
        _display.ClearReceivedCalls();

        // DisplaysChanged is announced for discovery too; reloading on every one of them would
        // restart the song on the television whenever a device appeared on the network.
        await _broker.PublishAsync(new DisplaysChanged());
        await Task.Delay(100);

        await _display.DidNotReceive().LoadAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACastSessionAppearingWithNothingPlaying_LoadsNothing()
    {
        ConnectCast();
        await Task.Delay(100);

        // Nothing is open to hand over, and opening one to fill the silence would start an
        // ffmpeg for a song no one asked for.
        await _display.DidNotReceive().LoadAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Selecting a device, as the Screens dialog does: a connection and an announcement.</summary>
    /// <summary>A switch, not an addition: one display at a time, so the screen goes as the
    /// receiver arrives. The receiver first, or the screen's loss would park the song.</summary>
    private void ConnectCast()
    {
        _display.ConnectedDeviceId.Returns("Living Room TV");
        _display.SessionId.Returns(Guid.NewGuid());
        ConnectScreens(0);

        _broker.Announce(new DisplaysChanged());
    }

    private async Task<bool> WaitForCastLoadAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            if (_display.ReceivedCalls().Any(c =>
                    c.GetMethodInfo().Name == nameof(IDisplayProvider.LoadAsync)))
                return true;

            await Task.Delay(10);
        }

        return false;
    }

    [Fact]
    public async Task Load_OpensAHostStream_AndSendsItsUrlToTheScreens()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);

        await _mediaStreams.Received(1).OpenAsync(media.FilePath, TimeSpan.Zero, 0, 0, Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>());

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

        // One host transcode per song is the whole reason ffmpeg moved off the screens.
        await _mediaStreams.Received(1).OpenAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>());
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
            .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>())
            .Returns<MediaStreamSession>(_ => throw new FileNotFoundException("gone"));

        var (performance, media) = CreatePerformance();

        // No stream means no playback: the screen plays the host's transcode, so a load that
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

    // One outcome, whatever the venue says, because a receiver's idea of where it was is its own:
    // it buffers seconds ahead, reports a position it has not reached, and comes back having
    // forgotten the session. Starting the turn again is the one thing a host can predict.
    [Fact]
    public async Task ScreenDisconnect_PausesAndRewindsToTheStart()
    {
        var performance = await PlayThenLoseAllScreensAsync(tick: true);

        Assert.True(await WaitForParkedAtStartAsync());
        Assert.Same(performance, _service.CurrentPerformance);
    }

    /// <summary>The song waits on the play button rather than lurching back to life under a singer
    /// who has stopped expecting it.</summary>
    [Fact]
    public async Task ScreenDisconnect_DoesNotAutoResume_WhenAScreenReturns()
    {
        await PlayThenLoseAllScreensAsync();
        Assert.True(await WaitForStateAsync(PlaybackState.Paused));

        ConnectScreens(1);
        RaiseScreenConnected();

        Assert.False(await WaitForStateAsync(PlaybackState.Playing));
        Assert.Equal(PlaybackState.Paused, _service.State);
    }

    /// <summary>Losing the picture is not losing the turn: the singer keeps their place.</summary>
    [Fact]
    public async Task ScreenDisconnect_KeepsTheSongLoaded()
    {
        await PlayThenLoseAllScreensAsync();

        Assert.True(await WaitForStateAsync(PlaybackState.Paused));
        Assert.NotNull(_service.CurrentPerformance);
        Assert.NotNull(_service.CurrentMedia);
    }

    /// <summary>Nothing is read from the venue any more, so a console with none behaves the same.</summary>
    [Fact]
    public async Task ScreenDisconnect_BehavesTheSame_WithNoVenueSelected()
    {
        _venuesService.ReadSelectedVenueAsync().Returns((Venue?)null);
        await PlayThenLoseAllScreensAsync(tick: true);

        Assert.True(await WaitForParkedAtStartAsync());
    }

    /// <summary>A key change opens its stream at the playhead, so parked at the start the song sits
    /// behind it: handed that stream, the returning screen resumed where the key changed.</summary>
    [Fact]
    public async Task ScreenReconnect_ParkedBehindARebuiltStream_ReopensItAtTheStart()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.SeekAsync(TimeSpan.FromSeconds(45));
        await _service.SetPitchAsync(2);
        Assert.True(await WaitForStreamsOpenedAsync(2));

        ConnectScreens(0);
        Assert.True(await WaitForParkedAtStartAsync());
        _screenServer.ClearReceivedCalls();

        ConnectScreens(1);

        Assert.True(await WaitForStreamsOpenedAsync(3));
        Assert.True(await WaitForBroadcastAsync<LoadMediaCommand>());
        var load = LastBroadcast<LoadMediaCommand>();
        Assert.NotNull(load);
        Assert.Equal("http://host/media/stream-3/stream.m3u8", load.StreamUrl);
        Assert.Equal(TimeSpan.Zero, load.StreamStartOffset);
        Assert.Equal(2, _service.Pitch);
    }

    /// <summary>A stream that already opens at the start holds the parked playhead, so a returning
    /// screen takes it rather than paying for a second transcode.</summary>
    [Fact]
    public async Task ScreenReconnect_ParkedOnAStreamFromTheStart_ReusesIt()
    {
        await PlayThenLoseAllScreensAsync(tick: true);
        Assert.True(await WaitForParkedAtStartAsync());
        _screenServer.ClearReceivedCalls();

        ConnectScreens(1);

        Assert.True(await WaitForBroadcastAsync<LoadMediaCommand>());
        Assert.Equal("http://host/media/stream-1/stream.m3u8", LastBroadcast<LoadMediaCommand>()?.StreamUrl);
        Assert.Equal(1, _streamsOpened);
    }

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

    // --- stems a display mixes for itself ---

    /// <summary>Arms the stand-in renderer to answer with stems, as a kit's own would.</summary>
    /// <remarks>What a renderer decides is its own business and is tested where it lives; these
    /// only care that the host asks it, passes on what it says, and drives it afterwards.</remarks>
    private void RendererOffersStems(params AudioTrackRole[] roles)
        => _renderer.Rendition = new MediaRendition
        {
            Stems = [.. roles.Select((role, i) => new StemSource(
                i,
                role,
                $"http://host/media/stems/stem{i}.ogg",
                role == AudioTrackRole.Music ? AudioMix.MaxVolume : 50))],
            SeekableInPlace = true,
        };

    /// <summary>A renderer that answers only when a test has armed it, and claims everything.</summary>
    private sealed class StubRenderer : IMediaRenderer
    {
        public MediaRendition? Rendition { get; set; }

        public MediaRenderRequest? LastRequest { get; private set; }

        public bool CanRender(string filePath) => true;

        public Task<MediaRendition?> RenderAsync(MediaRenderRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            // Null falls through to the fallback, which is every test that never armed this.
            return Task.FromResult(Rendition);
        }
    }

    private void TracksAre(params AudioTrackRole[] roles)
        => _audioTracks
            .ReadTracksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<AudioTrack>>(
                [.. roles.Select((role, i) => new AudioTrack(i, role, role.ToString()))]);

    [Fact]
    public async Task Load_HandsOverWhateverTheRendererAnswered()
    {
        // What to offer is the renderer's decision and is tested where that decision lives. The
        // host's job is to ask, and to send on what it was given rather than rebuilding it.
        RendererOffersStems(AudioTrackRole.Music, AudioTrackRole.Lead);

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        var load = LastBroadcast<LoadMediaCommand>();
        Assert.NotNull(load);
        Assert.Equal(
            [AudioTrackRole.Music, AudioTrackRole.Lead],
            load.Stems.Select(stem => stem.Role));

        // Nothing was encoded for it, so there is no stream to name.
        Assert.Null(load.StreamUrl);
        Assert.False(await WaitForStreamsOpenedAsync(1));
    }

    [Fact]
    public async Task Load_TellsTheRendererTheKeyTheSpeedAndWhatTheDisplayCanTake()
    {
        // Everything the renderer needs to decide with. Sent wrong, a kit would be handed to a
        // screen as raw stems at the written key while the song's clock ran at the asked-for rate.
        var (performance, media) = CreatePerformance();
        performance.Pitch = 2;
        performance.Tempo = -30;

        await _service.LoadAsync(performance, media);

        var request = _renderer.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(media.FilePath, request.FilePath);
        Assert.Equal(2, request.Pitch);
        Assert.Equal(-30, request.Tempo);
        Assert.True(request.Target.MixesStems);
    }

    [Fact]
    public async Task Load_TellsTheRendererNothingMixes_WhenTheDisplayCannot()
    {
        // A device hearing the host's own mix needs the encode, so offering stems is waste.
        ConnectScreens(0);
        _display.ConnectedDeviceId.Returns("Living Room TV");
        _display.Devices.Returns([new DisplayDevice
        {
            Id = "Living Room TV",
            Name = "Living Room TV",
            IsConnected = true,
            SupportsAudio = true,
            SupportsStemMix = false,
        }]);

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        Assert.False(_renderer.LastRequest?.Target.MixesStems);
    }

    [Fact]
    public async Task SetLeadVolume_MovesTheStem_AndLeavesTheTranscodeAlone()
    {
        RendererOffersStems(AudioTrackRole.Music, AudioTrackRole.Lead);

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.SetLeadVolumeAsync(55);

        var moved = LastBroadcast<SetStemVolumeCommand>();
        Assert.NotNull(moved);
        Assert.Equal(AudioTrackRole.Lead, moved.Role);
        Assert.Equal(55, moved.Volume);

        // The whole point: no ffmpeg, so the room hears the change with no hole in the song.
        Assert.False(await WaitForStreamsOpenedAsync(1));
        Assert.Equal(PlaybackState.Playing, _service.State);
    }

    [Fact]
    public async Task SetLeadVolume_RebuildsTheStream_WhenNothingIsMixingForUs()
    {
        // The renderer answered with a stream rather than stems, so the levels are baked into it
        // and only a new encode can move them.
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.SetLeadVolumeAsync(55);

        Assert.True(await WaitForStreamsOpenedAsync(2));
        Assert.Null(LastBroadcast<SetStemVolumeCommand>());
    }

    private TCommand? LastBroadcast<TCommand>() where TCommand : class, IScreenCommand
        => _screenServer.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync))
            .Select(c => c.GetArguments().FirstOrDefault() as TCommand)
            .LastOrDefault(c => c is not null);

    /// <summary>The screen defines the clock, so its own id is what reports against it.</summary>
    private void RaiseScreenState(TimeSpan position, TimeSpan? sampledAgo = null)
        => _screenServer.StateReceived += Raise.EventWith(_screenServer, new ScreenStateReceivedEventArgs
        {
            ScreenId = "Screen 1",
            State = new ScreenPlaybackState
            {
                StreamUrl = "http://192.168.1.10:5251/media/abc123/stream.m3u8",
                IsPlaying = true,
                Position = position,
                Duration = TimeSpan.FromMinutes(4),
                SampledAtUtc = DateTime.UtcNow - (sampledAgo ?? TimeSpan.Zero),
            },
        });

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(10);
    }

    // The picture is drawn by the screens provider on hearing PlaybackChanged, which leaves the
    // broker's thread, so it lands a moment after the call that moved the program returns.
    private async Task<bool> WaitForBroadcastAsync<TCommand>(Func<TCommand, bool>? matches = null)
        where TCommand : IScreenCommand
    {
        for (var i = 0; i < 50; i++)
        {
            if (_screenServer.ReceivedCalls().Any(c =>
                    c.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync) &&
                    c.GetArguments().FirstOrDefault() is TCommand command &&
                    (matches?.Invoke(command) ?? true)))
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
    // The loss handler pauses and only then rewinds, so waiting on Paused alone reads the
    // position in between.
    private async Task<bool> WaitForParkedAtStartAsync()
    {
        for (var i = 0; i < 50; i++)
        {
            if (_service.State == PlaybackState.Paused && _service.Position == TimeSpan.Zero) return true;
            await Task.Delay(10);
        }

        return false;
    }

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

    // The host waits out the fade it asks for, so a receiver that cuts dead would otherwise buy
    // the room five seconds of silence before the queue moved on.
    [Fact]
    public async Task StopAsync_StopsInstantly_WhenNothingConnectedCanFade()
    {
        ConnectScreens(0);
        _display.ConnectedDeviceId.Returns("Living Room TV");
        _display.Devices.Returns([new DisplayDevice
        {
            Id = "Living Room TV",
            Name = "Living Room TV",
            IsConnected = true,
            SupportsAudio = true,
            SupportsVideo = true,
            SupportsFade = false,
        }]);

        var service = MakeService(TimeSpan.FromSeconds(30));
        var (performance, media) = CreatePerformance();

        await service.LoadAsync(performance, media);
        await service.PlayAsync();

        // A thirty-second fade: if it were waited out, this call could not return in time.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.StopAsync();
        stopwatch.Stop();

        await _display.Received(1).StopAsync(TimeSpan.Zero, Arg.Any<CancellationToken>());
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"the stop waited {stopwatch.Elapsed} on a device that cannot fade");
    }

    /// <summary>A provider can report a connection before it has listed the device behind it.
    /// Over-waiting is a pause nobody hears; under-waiting cuts a song off mid-word.</summary>
    [Fact]
    public async Task StopAsync_KeepsTheFade_WhenTheConnectedDeviceIsNotListedYet()
    {
        ConnectScreens(0);
        _display.ConnectedDeviceId.Returns("tv-1");
        _display.Devices.Returns([]);

        var service = MakeService(TimeSpan.FromMilliseconds(80));
        var (performance, media) = CreatePerformance();

        await service.LoadAsync(performance, media);
        await service.PlayAsync();
        await service.StopAsync();

        await _display.Received(1).StopAsync(TimeSpan.FromMilliseconds(80), Arg.Any<CancellationToken>());
    }

    /// <summary>A screen owns its mixer, so the fade it was asked for is still honoured.</summary>
    [Fact]
    public async Task StopAsync_KeepsTheFade_WhenAScreenIsCarryingTheSong()
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

    /// <summary>Timer.Dispose does not wait out a callback already running, so a tick can arrive
    /// after the pause stopped the clock. Calling it directly is that late callback.</summary>
    [Fact]
    public async Task TickAsync_ArrivingAfterAPause_DoesNotRunTheSongToItsEnd()
    {
        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMilliseconds(1);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.PauseAsync();
        await Task.Delay(10);

        await _service.TickAsync();

        Assert.Equal(PlaybackState.Paused, _service.State);
        Assert.Same(performance, _service.CurrentPerformance);
        await _performanceService.DidNotReceive().DequeueAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
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

        // Seek stops and restarts the clock, so concurrent seeks race that swap: unsynchronised,
        // one assigns a Timer the other has already replaced, orphaned and never disposed.
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

    /// <summary>A silent still leaves the bed alone, but the ended song already put it down.</summary>
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
    // duration. One without a duration would hold the main channel all night.
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

    private void VenueBranding(Guid? mediaId, string format = "PNG",
        ImageScaling? venueScaling = null, ImageScaling imageScaling = ImageScaling.Fit)
    {
        _venuesService.ReadSelectedVenueAsync().Returns(new Venue
        {
            Id = Guid.NewGuid(),
            Name = "Test Venue",
            Settings = new Venue.VenueSettings
            {
                BrandingImageMediaId = mediaId,
                BrandingImageScaling = venueScaling,
            },
        });

        if (mediaId is { } id)
        {
            _mediaService.ReadAsync(id).Returns(new Media
            {
                Id = id,
                FilePath = "/media/brand.png",
                Title = "Venue Card",
                Status = MediaStatus.Ready,
                ImageScaling = imageScaling,
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

        Assert.True(await WaitForBroadcastAsync<ShowImageCommand>());
        await _mediaStreams.DidNotReceive().OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>());
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

    /// <summary>Scaling belongs to a picture; the library's answer stands unless overridden.</summary>
    [Fact]
    public async Task TheCard_VenueSaidNothingAboutScaling_TakesTheImagesOwn()
    {
        VenueBranding(Guid.NewGuid(), venueScaling: null, imageScaling: ImageScaling.Original);

        _broker.Announce(new SelectedVenueChanged());

        bool Scaled() => _screenServer.ReceivedCalls().Any(call =>
            call.GetArguments().FirstOrDefault() is ShowImageCommand command
            && command.Scaling == ImageScaling.Original);

        await WaitForAsync(Scaled);

        Assert.True(Scaled(), "The image's own scaling never reached the screens.");
    }

    /// <summary>The same picture cards two rooms with different screen shapes; the venue wins.</summary>
    [Fact]
    public async Task TheCard_VenueChoseScaling_UsesItOverTheImagesOwn()
    {
        VenueBranding(Guid.NewGuid(), venueScaling: ImageScaling.Fill, imageScaling: ImageScaling.Original);

        _broker.Announce(new SelectedVenueChanged());

        bool Scaled() => _screenServer.ReceivedCalls().Any(call =>
            call.GetArguments().FirstOrDefault() is ShowImageCommand command
            && command.Scaling == ImageScaling.Fill);

        await WaitForAsync(Scaled);

        Assert.True(Scaled(), "The venue's scaling never reached the screens.");
    }

    /// <summary>An edit to the venue's card updates the screen now, not on the next transition.</summary>
    [Fact]
    public async Task VenueChanged_NothingPlaying_PutsTheNewCardUpAtOnce()
    {
        var brandingId = Guid.NewGuid();
        VenueBranding(brandingId);

        _broker.Announce(new SelectedVenueChanged());

        // The shared helper gives up quietly, so the assertion has to be made after it rather
        // than left to it.
        bool ShowsTheNewCard() => _screenServer.ReceivedCalls().Any(call =>
            call.GetArguments().FirstOrDefault() is ShowImageCommand command
            && command.Url.Contains(brandingId.ToString()));

        await WaitForAsync(ShowsTheNewCard);

        Assert.True(ShowsTheNewCard(), "The venue's new card never reached the screens.");
    }

    /// <summary>A still over a singer is worse than stale; raised once the song ends.</summary>
    [Fact]
    public async Task VenueChanged_WhileSomeoneIsSinging_LeavesTheirSongAlone()
    {
        VenueBranding(Guid.NewGuid());

        var (performance, media) = CreatePerformance();
        media.Duration = TimeSpan.FromMinutes(3);

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        _screenServer.ClearReceivedCalls();

        _broker.Announce(new SelectedVenueChanged());
        await Task.Delay(60);

        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<ShowImageCommand>());
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

        Assert.True(await WaitForBroadcastAsync<ShowImageCommand>(c => c.Url.Contains(brandingId.ToString())));
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

        Assert.True(await WaitForBroadcastAsync<HideImageCommand>());
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

        Assert.True(await WaitForBroadcastAsync<HideImageCommand>());
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<ShowImageCommand>());
    }

    [Fact]
    public void CurrentProgram_BeforeAnythingIsLoaded_IsIdle()
        => Assert.IsType<PlaybackProgram.Idle>(_service.CurrentProgram);

    [Fact]
    public async Task CurrentProgram_ASongLoaded_IsThatSongAndItsTurn()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);

        var playing = Assert.IsType<PlaybackProgram.Playing>(_service.CurrentProgram);
        Assert.Same(media, playing.Media);
        Assert.Same(performance, playing.Performance);
    }

    /// <summary>Nothing is performed, so the display must not be left believing a song is on.</summary>
    [Fact]
    public async Task CurrentProgram_ALoadThatFails_IsIdleAgain()
    {
        var (performance, media) = CreatePerformance();
        FailTheStreamOpen();

        await Assert.ThrowsAsync<KHostException>(() => _service.LoadAsync(performance, media));

        Assert.IsType<PlaybackProgram.Idle>(_service.CurrentProgram);
    }

    [Fact]
    public async Task CurrentProgram_ASongEnds_IsIdle()
    {
        await EndAPerformanceAsync();

        Assert.IsType<PlaybackProgram.Idle>(_service.CurrentProgram);
    }

    [Fact]
    public async Task CurrentProgram_AVideoAd_IsPlayingWithNobodysTurn()
    {
        var ad = CreateAd();

        await _service.PlayAdAsync(ad);

        var playing = Assert.IsType<PlaybackProgram.Playing>(_service.CurrentProgram);
        Assert.Same(ad, playing.Media);
        Assert.Null(playing.Performance);
    }

    /// <summary>The screen holds no library, so the program carries the picture's address and scaling.</summary>
    [Fact]
    public async Task CurrentProgram_AStillAd_CarriesItsImageAndScaling()
    {
        _mediaStreams.BuildImageUrl(Arg.Any<Guid>()).Returns(call => $"http://host/media/image/{call.Arg<Guid>()}");
        var still = CreateStillAd();
        still.ImageScaling = ImageScaling.Fill;

        await _service.PlayAdAsync(still);

        var shown = Assert.IsType<PlaybackProgram.AdStill>(_service.CurrentProgram);
        Assert.Equal($"http://host/media/image/{still.Id}", shown.ImageUrl);
        Assert.Equal(ImageScaling.Fill, shown.Scaling);
    }

    /// <summary>An audio-only spot has no picture of its own, so the venue's card stays up.</summary>
    [Fact]
    public async Task CurrentProgram_AnAudioOnlyAd_IsIdle()
    {
        await _service.PlayAdAsync(new AdPlayback { Audio = CreateAudio(), Duration = TimeSpan.FromSeconds(12) });

        Assert.True(_service.IsPlayingAd);
        Assert.IsType<PlaybackProgram.Idle>(_service.CurrentProgram);
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

        Assert.True(await WaitForBroadcastAsync<ShowImageCommand>());
        await _screenServer.Received().BroadcastCommandAsync(Arg.Any<LoadBackgroundCommand>());
        await _breakMusic.Received(1).SuspendAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlayAdAsync_AudioOnly_LeavesTheScreenAlone()
    {
        var ad = new AdPlayback { Audio = CreateAudio(), Duration = TimeSpan.FromSeconds(12) };

        Assert.True(await _service.PlayAdAsync(ad));

        // Long enough for the screens provider to have heard the announce and drawn nothing.
        await Task.Delay(60);

        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<ShowImageCommand>());
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<LoadMediaCommand>());
        await _screenServer.Received().BroadcastCommandAsync(Arg.Any<LoadBackgroundCommand>());
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
            Arg.Any<int>(), 0, Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>());
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
        await _screenServer.Received().BroadcastCommandAsync(Arg.Any<StopBackgroundCommand>());
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
    // channel, and only this hands it back. Otherwise a voiceover plays under the singer.
    [Fact]
    public async Task LoadAsync_WhileAnAdWithItsOwnAudioIsPlaying_HandsTheChannelBack()
    {
        await _service.PlayAdAsync(new AdPlayback { Audio = CreateAudio(), Duration = TimeSpan.FromSeconds(12) });
        _screenServer.ClearReceivedCalls();
        _mediaStreams.ClearReceivedCalls();

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        await _screenServer.Received().BroadcastCommandAsync(Arg.Any<StopBackgroundCommand>());
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
            Arg.Any<int>(), 0, Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>());
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

        Assert.True(await WaitForBroadcastAsync<ShowImageCommand>(c => c.Scaling == ImageScaling.Fill));
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

        Assert.True(await WaitForBroadcastAsync<ShowImageCommand>(c => c.Scaling == ImageScaling.Stretch));
    }

    [Theory]
    [InlineData(9, 6)]
    [InlineData(-9, -6)]
    [InlineData(3, 3)]
    public async Task SetPitch_ClampsToTheSupportedRange(int requested, int expected)
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        await _service.SetPitchAsync(requested);

        Assert.Equal(expected, _service.Pitch);
    }

    [Fact]
    public async Task SetPitch_ReopensTheTranscodeAtThePlayhead_WithTheNewPitch()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.SeekAsync(TimeSpan.FromSeconds(30));

        await _service.SetPitchAsync(2);

        // ffmpeg fixes its filter graph at process start, so only a fresh transcode carries the
        // change; opening at the playhead is what stops the song restarting.
        Assert.True(await WaitForStreamsOpenedAsync(2));
        await _mediaStreams.Received(1).OpenAsync(
            media.FilePath, TimeSpan.FromSeconds(30), 2, 0, Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A pause landing while the new transcode starts must win: resuming afterwards plays
    /// the room a song the console shows as paused.</summary>
    [Fact]
    public async Task SetPitch_HostPausesWhileTheStreamRebuilds_DoesNotResume()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        var gate = new TaskCompletionSource();
        _screenServer.BroadcastCommandAsync(Arg.Any<IScreenCommand>()).Returns(async call =>
        {
            if (call.Arg<IScreenCommand>() is LoadMediaCommand)
                await gate.Task;
        });
        _screenServer.ClearReceivedCalls();

        await _service.SetPitchAsync(2);

        // Parked on the new stream's load, before the decision to resume.
        Assert.True(await WaitForBroadcastAsync<LoadMediaCommand>());

        await _service.PauseAsync();

        // The rebuild announces on its way out, which is the end of everything it sends.
        var finished = 0;
        using var subscription = _broker.Subscribe<PlaybackChanged>(_ => Interlocked.Increment(ref finished));
        gate.SetResult();
        await WaitForAsync(() => Volatile.Read(ref finished) > 0);

        Assert.True(Volatile.Read(ref finished) > 0);
        Assert.Equal(PlaybackState.Paused, _service.State);
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<PlayCommand>());
    }

    [Fact]
    public async Task SetPitch_SendsTheNewStreamToTheScreens_AndKeepsPlaying()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.SetPitchAsync(-2);

        Assert.True(await WaitForStreamsOpenedAsync(2));

        // The screens hold no decoder: without the new URL they sit on the stream that just died.
        var load = LastBroadcast<LoadMediaCommand>();
        Assert.NotNull(load);
        Assert.Equal("http://host/media/stream-2/stream.m3u8", load.StreamUrl);
        Assert.Equal(PlaybackState.Playing, _service.State);
    }

    [Fact]
    public async Task SetPitch_ClosesThePreviousTranscode()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.SetPitchAsync(1);

        Assert.True(await WaitForStreamsOpenedAsync(2));

        // Two ffmpegs on one song is the cost of every key change, all night.
        await _mediaStreams.Received().CloseAsync("stream-1");
    }

    [Fact]
    public async Task SetPitch_LeavesAPausedSongPaused()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.PauseAsync();

        await _service.SetPitchAsync(4);

        Assert.True(await WaitForStreamsOpenedAsync(2));

        // Rebuilding the stream must not start the song over the top of a host who paused it.
        Assert.Equal(PlaybackState.Paused, _service.State);
    }

    [Fact]
    public async Task SetPitch_DoesNotOpenATranscode_WhenNothingIsLoaded()
    {
        await _service.SetPitchAsync(3);

        Assert.Equal(3, _service.Pitch);
        Assert.False(await WaitForStreamsOpenedAsync(1, attempts: 10));
    }

    [Fact]
    public async Task SetPitch_DoesNothing_WhenTheValueHasNotChanged()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.SetPitchAsync(2);
        Assert.True(await WaitForStreamsOpenedAsync(2));

        await _service.SetPitchAsync(2);

        // A repeat press on a clamped end must not cost the song another hole.
        Assert.False(await WaitForStreamsOpenedAsync(3, attempts: 10));
    }

    [Fact]
    public async Task SetPitch_AnnouncesBeforeTheTranscodeIsRebuilt()
    {
        // A settle long enough that the reopen cannot have run: the announcement under test is
        // the one landing before ffmpeg is touched.
        using var _service = MakeService(TimeSpan.Zero, TimeSpan.FromSeconds(30));
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        var announcements = 0;
        using var subscription = _broker.Subscribe<PlaybackChanged>(_ => announcements++);

        await _service.SetPitchAsync(5);

        // Waiting out the settle would leave the readout a beat behind every press.
        Assert.Equal(1, announcements);
        Assert.Equal(5, _service.Pitch);
    }

    [Fact]
    public async Task SetPitch_CollapsesRepeatedPresses_IntoOneReopen()
    {
        // The only test that uses a real delay: the collapsing is what it is measuring.
        using var service = MakeService(TimeSpan.Zero, TimeSpan.FromMilliseconds(200));
        var (performance, media) = CreatePerformance();
        await service.LoadAsync(performance, media);
        await service.PlayAsync();

        await service.SetPitchAsync(1);
        await service.SetPitchAsync(2);
        await service.SetPitchAsync(3);

        Assert.True(await WaitForStreamsOpenedAsync(2));
        await Task.Delay(300);

        // Three presses, one hole in the song, and it lands on the key the host settled on.
        Assert.Equal(2, _streamsOpened);
        await _mediaStreams.Received(1).OpenAsync(
            media.FilePath, Arg.Any<TimeSpan>(), 3, 0, Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Load_DoesNotCarryTheKeyToTheNextSinger()
    {
        var (firstPerformance, firstMedia) = CreatePerformance();
        await _service.LoadAsync(firstPerformance, firstMedia);
        await _service.SetPitchAsync(4);

        var (nextPerformance, nextMedia) = CreatePerformance();
        await _service.LoadAsync(nextPerformance, nextMedia);

        // The next performance brings its own key rather than inheriting the console's.
        Assert.Equal(0, _service.Pitch);
        await _mediaStreams.Received().OpenAsync(
            nextMedia.FilePath, TimeSpan.Zero, 0, 0, Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stop_ResetsThePitch()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.SetPitchAsync(-3);

        await _service.StopAsync();

        Assert.Equal(0, _service.Pitch);
    }

    [Fact]
    public async Task Load_TakesTheKeyFromThePerformance()
    {
        var (performance, media) = CreatePerformance();
        performance.Pitch = -3;

        await _service.LoadAsync(performance, media);

        // A performance re-queued from history carries the key it was sung in.
        Assert.Equal(-3, _service.Pitch);
        await _mediaStreams.Received(1).OpenAsync(
            media.FilePath, TimeSpan.Zero, -3, 0, Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPitch_RecordsTheKeyOnThePerformance()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.SetPitchAsync(-2);

        // The history is read back from the row; a key not written there is lost at song end.
        await _performanceService.Received().UpdateAsync(
            Arg.Is<Performance>(p => p.Id == performance.Id && p.Pitch == -2));
    }

    [Fact]
    public async Task SetPitch_RecordsTheKeyBeforeTheSettleElapses()
    {
        // A song that ends inside the settle window must not lose the key the singer just found.
        using var _service = MakeService(TimeSpan.Zero, TimeSpan.FromSeconds(30));
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.SetPitchAsync(4);

        await _performanceService.Received().UpdateAsync(
            Arg.Is<Performance>(p => p.Pitch == 4));
        Assert.Equal(1, _streamsOpened);
    }

    [Fact]
    public async Task SetPitch_RecordsNothing_ForAnAd()
    {
        var ad = new Media
        {
            Id = Guid.NewGuid(),
            FilePath = "/media/ad.mp4",
            Title = "Ad",
            Status = MediaStatus.Ready,
            Duration = TimeSpan.FromSeconds(20),
        };
        await _service.PlayAdAsync(ad);

        await _service.SetPitchAsync(3);

        // An ad is nobody's turn and nobody's key, and it has no row to write one on.
        await _performanceService.DidNotReceive().UpdateAsync(Arg.Any<Performance>());
    }

    [Theory]
    [InlineData(80, 50)]
    [InlineData(-80, -50)]
    [InlineData(-25, -25)]
    public async Task SetTempo_ClampsToTheSupportedRange(int requested, int expected)
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);

        await _service.SetTempoAsync(requested);

        Assert.Equal(expected, _service.Tempo);
    }

    [Fact]
    public async Task SetTempo_ReopensTheTranscodeAtTheNewTempo()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.SeekAsync(TimeSpan.FromSeconds(30));

        await _service.SetTempoAsync(-20);

        Assert.True(await WaitForStreamsOpenedAsync(2));
        await _mediaStreams.Received(1).OpenAsync(
            media.FilePath, TimeSpan.FromSeconds(30), 0, -20, Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetTempo_AndSetPitchTogether_CostOneReopen()
    {
        using var service = MakeService(TimeSpan.Zero, TimeSpan.FromMilliseconds(200));
        var (performance, media) = CreatePerformance();
        await service.LoadAsync(performance, media);
        await service.PlayAsync();

        await service.SetPitchAsync(2);
        await service.SetTempoAsync(-10);

        Assert.True(await WaitForStreamsOpenedAsync(2));
        await Task.Delay(300);

        // One settle covers both, or finding a key and then a speed takes two holes in the song.
        Assert.Equal(2, _streamsOpened);
        await _mediaStreams.Received(1).OpenAsync(
            media.FilePath, Arg.Any<TimeSpan>(), 2, -10, Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScreenReport_ExtrapolatesSongTimeByTheTempo()
    {
        var (performance, media) = CreatePerformance();
        performance.Tempo = 50;
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        RaiseScreenState(TimeSpan.FromSeconds(60), sampledAgo: TimeSpan.FromSeconds(2));

        // The report is two seconds old in wall time, and at 1.5x the song moved three seconds in
        // it. Extrapolating one-to-one would leave the host's playhead a second behind the room.
        Assert.InRange(
            _service.Position,
            TimeSpan.FromSeconds(62.9),
            TimeSpan.FromSeconds(63.3));
    }

    [Fact]
    public async Task Load_TellsTheScreenTheTempo()
    {
        var (performance, media) = CreatePerformance();
        performance.Tempo = -30;

        await _service.LoadAsync(performance, media);

        // The page counts stream seconds, so it cannot recover song time without this.
        Assert.Equal(-30, LastBroadcast<LoadMediaCommand>()?.Tempo);
    }

    [Fact]
    public async Task Load_TellsTheReceiverTheTempo()
    {
        ConnectScreens(0);
        _display.ConnectedDeviceId.Returns("Living Room TV");

        var (performance, media) = CreatePerformance();
        performance.Tempo = -30;

        await _service.LoadAsync(performance, media);

        // It keeps its own clock in stream seconds, so it cannot recover song time without this.
        await _display.Received(1).LoadAsync(
            Arg.Is<LoadMediaCommand>(c => c.Tempo == -30), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Load_TakesTheTempoFromThePerformance()
    {
        var (performance, media) = CreatePerformance();
        performance.Tempo = -40;

        await _service.LoadAsync(performance, media);

        Assert.Equal(-40, _service.Tempo);
        await _mediaStreams.Received(1).OpenAsync(
            media.FilePath, TimeSpan.Zero, 0, -40, Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetTempo_RecordsTheSpeedOnThePerformance()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.SetTempoAsync(15);

        await _performanceService.Received().UpdateAsync(
            Arg.Is<Performance>(p => p.Id == performance.Id && p.Tempo == 15));
    }

    [Fact]
    public async Task Load_FindsTheVoicesInAMultiTrackFile()
    {
        var (performance, media) = CreatePerformance();
        GiveThreeTracks(media);

        await _service.LoadAsync(performance, media);

        Assert.Equal(3, _service.AudioTracks.Count);
        await _mediaStreams.Received(1).OpenAsync(
            media.FilePath, TimeSpan.Zero, 0, 0,
            Arg.Is<AudioMix?>(m => m != null && m.IsMixable), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Load_MixesNothing_ForAnOrdinarySingleTrackSong()
    {
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);

        // Most songs are this. A graph built for one stream is only more ways to fail.
        Assert.Empty(_service.AudioTracks);
        await _mediaStreams.Received(1).OpenAsync(
            media.FilePath, TimeSpan.Zero, 0, 0, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Load_StartsWithTheLeadVocalOutOfTheWay()
    {
        var (performance, media) = CreatePerformance();
        GiveThreeTracks(media);

        await _service.LoadAsync(performance, media);

        // The singer is there to replace it.
        Assert.Equal(0, _service.LeadVolume);
    }

    [Fact]
    public async Task Load_TakesTheBackingLevelFromTheMachineSetting_WhenNobodyHasMixedIt()
    {
        using var service = MakeService(TimeSpan.Zero, defaultBackingVolume: 65);
        var (performance, media) = CreatePerformance();
        performance.BackingVolume = null;

        await service.LoadAsync(performance, media);

        Assert.Equal(65, service.BackingVolume);
    }

    [Fact]
    public async Task Load_PrefersTheLevelTheSongWasSungAt_OverTheSetting()
    {
        using var service = MakeService(TimeSpan.Zero, defaultBackingVolume: 65);
        var (performance, media) = CreatePerformance();
        performance.LeadVolume = 40;
        performance.BackingVolume = 20;

        await service.LoadAsync(performance, media);

        // A performance re-queued from history comes back mixed the way it was sung.
        Assert.Equal(40, service.LeadVolume);
        Assert.Equal(20, service.BackingVolume);
    }

    [Theory]
    [InlineData(140, 100)]
    [InlineData(-20, 0)]
    [InlineData(35, 35)]
    public async Task SetLeadVolume_ClampsToWhatAFaderCanAsk(int requested, int expected)
    {
        var (performance, media) = CreatePerformance();
        GiveThreeTracks(media);
        await _service.LoadAsync(performance, media);

        await _service.SetLeadVolumeAsync(requested);

        Assert.Equal(expected, _service.LeadVolume);
    }

    [Fact]
    public async Task SetLeadVolume_ReopensTheTranscodeWithTheNewMix()
    {
        var (performance, media) = CreatePerformance();
        GiveThreeTracks(media);
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.SetLeadVolumeAsync(70);

        Assert.True(await WaitForStreamsOpenedAsync(2));
        await _mediaStreams.Received(1).OpenAsync(
            media.FilePath, Arg.Any<TimeSpan>(), 0, 0,
            Arg.Is<AudioMix?>(m => m != null && m.LeadVolume == 70), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetVolumes_RecordThemOnThePerformance()
    {
        var (performance, media) = CreatePerformance();
        GiveThreeTracks(media);
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.SetLeadVolumeAsync(25);
        await _service.SetBackingVolumeAsync(75);

        await _performanceService.Received().UpdateAsync(
            Arg.Is<Performance>(p => p.LeadVolume == 25 && p.BackingVolume == 75));
    }

    [Fact]
    public async Task SetVolumes_AndSetPitch_CostOneReopen()
    {
        using var service = MakeService(TimeSpan.Zero, TimeSpan.FromMilliseconds(200));
        var (performance, media) = CreatePerformance();
        GiveThreeTracks(media);
        await service.LoadAsync(performance, media);
        await service.PlayAsync();

        await service.SetLeadVolumeAsync(30);
        await service.SetBackingVolumeAsync(60);
        await service.SetPitchAsync(2);

        Assert.True(await WaitForStreamsOpenedAsync(2));
        await Task.Delay(300);

        // One settle covers the whole panel, or balancing a mix punches a hole per fader.
        Assert.Equal(2, _streamsOpened);
        await _mediaStreams.Received(1).OpenAsync(
            media.FilePath, Arg.Any<TimeSpan>(), 2, 0,
            Arg.Is<AudioMix?>(m => m != null && m.LeadVolume == 30 && m.BackingVolume == 60),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Tracks are read from the file the library row points at, which is the file that plays.
    /// </summary>
    [Fact]
    public async Task LoadAsync_ReadsItsTracksFromTheRow()
    {
        var (performance, media) = CreatePerformance();
        GiveThreeTracks(media);

        await _service.LoadAsync(performance, media);

        Assert.Equal(3, _service.AudioTracks.Count);
        await _audioTracks.Received(1).ReadTracksAsync(media.FilePath, Arg.Any<CancellationToken>());
    }

    /// <summary>Named and ordered as the real files are: music, then backing, then lead.</summary>
    private void GiveThreeTracks(Media media) =>
        _audioTracks.ReadTracksAsync(media.FilePath, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<AudioTrack>>(
        [
            new AudioTrack(0, AudioTrackRole.Music, "Instrumental"),
            new AudioTrack(1, AudioTrackRole.Backing, "Backing Vocal"),
            new AudioTrack(2, AudioTrackRole.Lead, "Lead Vocal"),
        ]);

    [Fact]
    public async Task Seek_RebuildsTheStream_WhenTheTargetIsBehindWhereItBegan()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.SeekAsync(TimeSpan.FromSeconds(200));

        // A rate change rebuilds the transcode at the playhead, so the stream now starts at 200s.
        await _service.SetPitchAsync(2);
        Assert.True(await WaitForStreamsOpenedAsync(2));

        await _service.SeekAsync(TimeSpan.FromSeconds(30));

        // The stream holds nothing before its own zero. Sending a bare seek would clamp to 200s
        // and the song would carry on from there, which is what a host reads as a dead scrub bar.
        Assert.True(await WaitForStreamsOpenedAsync(3));
        await _mediaStreams.Received(1).OpenAsync(
            media.FilePath, TimeSpan.FromSeconds(30), 2, 0, null, Arg.Any<CancellationToken>());
        Assert.Equal(TimeSpan.FromSeconds(30), _service.Position);
    }

    [Fact]
    public async Task Seek_SendsAPlainSeek_WhenTheTargetIsInsideTheStream()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        await _service.SeekAsync(TimeSpan.FromSeconds(90));

        // Forwards, or backwards inside a stream that began at zero, costs no ffmpeg restart.
        Assert.False(await WaitForStreamsOpenedAsync(2, attempts: 10));
        Assert.Equal(TimeSpan.FromSeconds(90), LastBroadcast<SeekCommand>()?.Position);
    }

    [Fact]
    public async Task Reopen_OpensTheReplacementBeforeClosingWhatIsPlaying()
    {
        var closedWhileOpening = new List<string>();
        var (performance, media) = CreatePerformance();

        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        // Records what had already been closed at the moment the replacement was asked for.
        _mediaStreams
            .When(m => m.OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(),
                                   Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>()))
            .Do(_ => closedWhileOpening.AddRange(
                _mediaStreams.ReceivedCalls()
                    .Where(c => c.GetMethodInfo().Name == nameof(IMediaStreamService.CloseAsync))
                    .Select(c => (string)c.GetArguments()[0]!)));

        await _service.SetPitchAsync(2);
        Assert.True(await WaitForStreamsOpenedAsync(2));

        // The screens play on from their buffer while ffmpeg spins up, and hear nothing at all if
        // the old transcode was already gone.
        Assert.DoesNotContain("stream-1", closedWhileOpening);
        await _mediaStreams.Received().CloseAsync("stream-1");
    }

    [Fact]
    public async Task Reopen_KeepsTheSongPlaying_WhenTheReplacementCannotBeBuilt()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();

        _mediaStreams
            .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(),
                       Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>())
            .Returns<MediaStreamSession>(_ => throw new InvalidOperationException("ffmpeg said no"));

        await _service.SetPitchAsync(2);
        await Task.Delay(200);

        // A rebuild that fails costs the host their change, not the song: the old session is still
        // open and the screens are still playing it.
        await _mediaStreams.DidNotReceive().CloseAsync("stream-1");
        Assert.Equal(PlaybackState.Playing, _service.State);
    }

    /// <summary>A rebuild resumes at the playhead it opened the stream at, and skips nothing.</summary>
    /// <remarks>The host used to skip forward by however long the rebuild took, since the room heard
    /// on from the old stream meanwhile. That lands on data ffmpeg has not written yet: the element
    /// takes over with barely a frame buffered, sounds for an instant and then starves — half a
    /// second of silence mid-song, for a sliver that would have passed unnoticed heard twice.
    /// Covering the window belongs to the transport, which knows what it is driving.</remarks>
    [Fact]
    public async Task Reopen_ResumesAtThePlayhead_AndSkipsNothing()
    {
        _display.ConnectedDeviceId.Returns("Living Room TV");

        // A slow rebuild, so any compensation would be larger than the clock's own resolution.
        _mediaStreams
            .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(),
                       Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(250);
                return new MediaStreamSession
                {
                    Id = $"stream-{Interlocked.Increment(ref _streamsOpened)}",
                    SourcePath = call.ArgAt<string>(0),
                    PlaylistUrl = $"http://host/media/stream-{_streamsOpened}/stream.m3u8",
                    StartOffset = call.ArgAt<TimeSpan>(1),
                    Pitch = call.ArgAt<int>(2),
                    Tempo = call.ArgAt<int>(3),
                };
            });

        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.SeekAsync(TimeSpan.FromSeconds(60));
        _display.ClearReceivedCalls();

        await _service.SetPitchAsync(2);
        Assert.True(await WaitForStreamsOpenedAsync(2));
        await Task.Delay(100);

        // Opened at the playhead, and the clock says the same: the stream's zero is where the song is.
        Assert.Equal(TimeSpan.FromSeconds(60), LastOpenedAt());
        Assert.Equal(TimeSpan.FromSeconds(60), _service.Position);

        // Nothing is skipped forward on anyone's behalf. A transport that cannot cover the rebuild
        // makes the difference up inside its own load, where it knows what it is driving.
        await _display.DidNotReceive().SeekAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seek_LandsWhereItWasAsked_NotPastIt()
    {
        var (performance, media) = CreatePerformance();
        await _service.LoadAsync(performance, media);
        await _service.PlayAsync();
        await _service.SeekAsync(TimeSpan.FromSeconds(200));
        await _service.SetPitchAsync(2);
        Assert.True(await WaitForStreamsOpenedAsync(2));

        await _service.SeekAsync(TimeSpan.FromSeconds(30));
        Assert.True(await WaitForStreamsOpenedAsync(3));

        // A rebuild driven by a seek must not carry the host past the place they asked for: the
        // room heard nothing there to avoid repeating.
        Assert.Equal(TimeSpan.FromSeconds(30), _service.Position);
    }

    private TimeSpan LastOpenedAt() => (TimeSpan)_mediaStreams.ReceivedCalls()
        .Last(c => c.GetMethodInfo().Name == nameof(IMediaStreamService.OpenAsync))
        .GetArguments()[1]!;

    [Fact]
    public async Task Reopen_LeavesTheOldSessionStanding_ForConsumersStillReadingIt()
    {
        // A real grace, so the assertion is about the delay rather than the eventual close.
        using var service = MakeService(TimeSpan.Zero, retireGrace: TimeSpan.FromSeconds(30));
        var (performance, media) = CreatePerformance();
        await service.LoadAsync(performance, media);
        await service.PlayAsync();

        await service.SetPitchAsync(2);
        Assert.True(await WaitForStreamsOpenedAsync(2));
        await Task.Delay(200);

        // Closing deletes the directory. A receiver has no second player to cross to, so it is
        // still fetching segments from the old one and would read the 404 body as media.
        await _mediaStreams.DidNotReceive().CloseAsync("stream-1");
    }

    private async Task<bool> WaitForStreamsOpenedAsync(int count, int attempts = 50)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (Volatile.Read(ref _streamsOpened) >= count) return true;
            await Task.Delay(10);
        }

        return false;
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

    /// <summary>The venue decides whether a queued name that differs from the singer's is shown.</summary>
    [Theory]
    [InlineData(true, "DJ P")]
    [InlineData(false, "Priya")]
    public async Task LoadAsync_ANameQueuedWithTheSong_IsHonouredOnlyIfTheVenueAllowsIt(bool allowAliases, string expected)
    {
        var (performance, media) = CreatePerformance();
        performance.SungAs = "DJ P";
        ArrangeSinger(performance.SingerId, "Priya");
        ArrangeVenue(allowAliases);

        await _service.LoadAsync(performance, media);

        Assert.Equal(expected, _service.CurrentSingerName);
    }

    [Fact]
    public async Task LoadAsync_NoNameQueuedWithTheSong_NamesTheSinger()
    {
        var (performance, media) = CreatePerformance();
        ArrangeSinger(performance.SingerId, "Priya");
        ArrangeVenue(allowAliases: true);

        await _service.LoadAsync(performance, media);

        Assert.Equal("Priya", _service.CurrentSingerName);
    }

    [Fact]
    public async Task LoadAsync_TheSingerIsGoneButTheNameWasRecorded_StillNamesThem()
    {
        // Why the name is kept on the row at all: a performance outlives the singer it points at.
        var (performance, media) = CreatePerformance();
        performance.SungAs = "DJ P";
        _queueService.Users.Returns([]);
        ArrangeVenue(allowAliases: false);

        await _service.LoadAsync(performance, media);

        Assert.Equal("DJ P", _service.CurrentSingerName);
    }

    [Fact]
    public async Task StoppingPlayback_TakesTheNameDownWithTheSong()
    {
        var (performance, media) = CreatePerformance();
        performance.SungAs = "DJ P";
        ArrangeSinger(performance.SingerId, "Priya");
        ArrangeVenue(allowAliases: true);
        await _service.LoadAsync(performance, media);

        await _service.StopAsync();

        Assert.Null(_service.CurrentSingerName);
    }

    private void ArrangeSinger(Guid id, string name)
        => _queueService.Users.Returns([new KHostUser { Id = id, Name = name }]);

    private void ArrangeVenue(bool allowAliases)
        => _venuesService.ReadSelectedVenueAsync().Returns(
            new Venue { Name = "The Bar", Settings = new Venue.VenueSettings { AllowAliases = allowAliases } });

}
