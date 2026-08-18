using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

public class PlaybackService : BaseService, IPlaybackService
{
    public sealed class ServiceOptions
    {
        public const string SectionName = "Playback";

        /// <summary>How long screens fade audio and video out for on stop. Zero stops instantly.</summary>
        public TimeSpan StopFadeDuration { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// How far ahead a scheduled start is placed. Every synced screen begins on this one
        /// instant instead of on whenever its own command arrived, so it has to be long enough to
        /// cover the slowest screen's command latency.
        /// </summary>
        public TimeSpan SyncStartLead { get; set; } = TimeSpan.FromMilliseconds(400);
    }

    private Timer? _timer;
    private DateTime _lastTick;
    private bool _resumeWhenScreenReturns;

    // Connect and disconnect both arrive as fire-and-forget continuations, so without this they
    // can interleave and a reconnect's resume races the disconnect's pause.
    private readonly SemaphoreSlim _screenSyncLock = new(1, 1);

    private IAnalyticsActivity? _sessionActivity;

    // The host-side transcode currently feeding the screens, if any.
    private MediaStreamSession? _stream;

    private readonly ISingerQueueService _singerQueueService;
    private readonly IPerformanceService _performanceService;
    private readonly IVenuesService _venuesService;
    private readonly IAnalyticsService _analytics;
    private readonly IScreenServer _screenServer;
    private readonly IMediaStreamService _mediaStreams;
    private readonly IScreenCoordinationService _screenCoordination;
    private readonly ServiceOptions _options;

    public Performance? CurrentPerformance { get; private set; }
    public Media? CurrentMedia { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    public TimeSpan Position { get; private set; }
    public Guid? CurrentlyPerformingUserId { get; private set; }
    public TimeSpan? StopFadeDuration { get; private set; }

    public PlaybackService(
        ILogger<PlaybackService> logger,
        ISingerQueueService singerQueueService,
        IPerformanceService performanceService,
        IVenuesService venuesService,
        IAnalyticsService analytics,
        IScreenServer screenServer,
        IMediaStreamService mediaStreams,
        IScreenCoordinationService screenGroup,
        IOptions<ServiceOptions> options)
        : base(logger)
    {
        _singerQueueService = singerQueueService;
        _performanceService = performanceService;
        _venuesService = venuesService;
        _analytics = analytics;
        _screenServer = screenServer;
        _mediaStreams = mediaStreams;
        _screenCoordination = screenGroup;
        _options = options.Value;

        _screenServer.ScreenConnected += OnScreenConnected;
        _screenServer.ScreenDisconnected += OnScreenDisconnected;
        _screenServer.StateReceived += OnScreenStateReceived;
    }

    public async Task<bool> HasConnectedScreenAsync()
    {
        try
        {
            await foreach (var _ in _screenServer.GetConnectedScreensAsync())
                return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to enumerate connected screens");
        }

        return false;
    }

    public async Task LoadAsync(Performance performance, Media media)
    {
        ResetState();

        CurrentPerformance = performance;
        CurrentMedia = media;
        Position = TimeSpan.Zero;

        _sessionActivity = _analytics.StartActivity(AnalyticActivities.Session);
        _sessionActivity.SetTag("media_id", media.Id);

        await _singerQueueService.MoveUserToStartAsync(performance.SingerId);
        _singerQueueService.LockTopSlot();

        Logger.LogInformation("Loading media '{Title}' for performance {PerformanceId}", media.Title, performance.Id);

        await SendToScreensAsync(await BuildLoadCommandAsync(media, TimeSpan.Zero));

        InvokeStateChanged();
    }

    public async Task PlayAsync()
    {
        if (CurrentPerformance is null || State == PlaybackState.Playing) return;

        // Without a screen there is no audio or video, but the position timer would still run
        // the performance to completion and rotate the singer away — burning their turn.
        if (!await HasConnectedScreenAsync())
        {
            Logger.LogWarning("Play refused: no screens are connected");
            return;
        }

        CurrentlyPerformingUserId = CurrentPerformance.SingerId;

        // Leaving Stopping here is what tells an in-flight StopAsync to abandon its completion.
        State = PlaybackState.Playing;
        StopFadeDuration = null;
        StartClock();

        _analytics.RecordPlaybackStateTransition(PlaybackState.Playing);

        Logger.LogInformation("Playback started for user {UserId}", CurrentPerformance.SingerId);

        await SendToScreensAsync(new PlayCommand());

        // After the play command, so a synced screen already has the stream open when it is told
        // which instant to start on.
        await PublishTimelineAsync(isPlaying: true, scheduleAhead: true);

        InvokeStateChanged();
    }

    public async Task PauseAsync()
    {
        if (State != PlaybackState.Playing)
            return;

        State = PlaybackState.Paused;
        StopClock();

        _analytics.RecordPlaybackStateTransition(PlaybackState.Paused);

        Logger.LogInformation("Playback paused at {Position}", Position);

        await SendToScreensAsync(new PauseCommand());
        await PublishTimelineAsync(isPlaying: false);

        InvokeStateChanged();

        return;
    }

    public async Task StopAsync()
    {
        if (State == PlaybackState.Stopping)
            return;

        // Screens can only fade while frames are flowing, so a paused stop is instant on the
        // screen — showing "Fading out…" here would just stall the UI over a blanked screen.
        var fade = State == PlaybackState.Playing ? _options.StopFadeDuration : TimeSpan.Zero;

        // Hold CurrentPerformance/CurrentMedia until the screens have finished fading, so the
        // UI can show the song winding down instead of blanking while it is still audible.
        StopClock();
        State = PlaybackState.Stopping;
        StopFadeDuration = fade > TimeSpan.Zero ? fade : null;

        Logger.LogInformation("Playback stopping (fade={Fade})", fade);

        InvokeStateChanged();

        await SendToScreensAsync(new StopCommand { FadeDuration = fade });

        if (fade > TimeSpan.Zero)
            await Task.Delay(fade);

        // Play or Load during the fade supersedes this stop.
        if (State != PlaybackState.Stopping)
            return;

        _analytics.RecordPlaybackStateTransition(PlaybackState.Stopped);

        ResetState();

        Logger.LogInformation("Playback stopped");

        await EndedAsync();

        InvokeStateChanged();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _screenServer.ScreenConnected -= OnScreenConnected;
            _screenServer.ScreenDisconnected -= OnScreenDisconnected;
            _screenServer.StateReceived -= OnScreenStateReceived;

            // _screenSyncLock is deliberately not disposed: a detached sync may still be holding
            // it at shutdown, and its Release would then throw.
            StopClock();
        }
    }

    // Raised while ScreenServerService holds the same non-reentrant lock GetConnectedScreensAsync
    // waits on, so this work must leave the hub thread or it deadlocks.
    private void OnScreenConnected(object? sender, ScreenConnectionEventArgs e) =>
        _ = Task.Run(SyncNewScreenAsync);

    private void OnScreenDisconnected(object? sender, ScreenConnectionEventArgs e) =>
        _ = Task.Run(HandleScreenLossAsync);

    /// <summary>
    /// A screen that connects mid-session has nothing loaded, so a bare PlayCommand would be
    /// rejected by the player while the UI happily showed playback running.
    /// </summary>
    private async Task SyncNewScreenAsync()
    {
        await _screenSyncLock.WaitAsync();
        try
        {
            var media = CurrentMedia;
            if (media is null)
                return;

            // A screen reconnecting under the same id overwrites its own tracked connection, so
            // the stale socket's disconnect is discarded and HandleScreenLossAsync never runs —
            // leaving State at Playing with nothing pending. Resume on either signal, not just
            // the flag, or the screen sits paused on the seek point while the UI plays on.
            bool resume = State == PlaybackState.Playing
                || (_resumeWhenScreenReturns && State == PlaybackState.Paused);
            _resumeWhenScreenReturns = false;

            // Reloading costs an FFProbe plus an ffmpeg spin-up. The clock is the only source of
            // truth for position, so letting it run across that gap resumes the screen behind
            // where the UI already is.
            StopClock();

            var position = Position;

            Logger.LogInformation("Screen connected; reloading '{Title}' at {Position}", media.Title, position);

            // Reuse the transcode that is already running rather than starting a second one —
            // one host stream feeding every screen is the whole point of moving ffmpeg here.
            await SendToScreensAsync(_stream is not null
                ? DescribeStream(media)
                : await BuildLoadCommandAsync(media, TimeSpan.Zero));

            if (position > TimeSpan.Zero)
                await SendToScreensAsync(new SeekCommand { Position = position });

            if (!resume)
            {
                // A screen that joins a paused song still needs to know where the group is parked.
                await PublishTimelineAsync(isPlaying: false, position);
                return;
            }

            // PlayAsync is a no-op while State is already Playing, so drive the screen directly
            // there and keep it for the paused case, which still needs the state transition.
            if (State == PlaybackState.Playing)
            {
                await SendToScreensAsync(new PlayCommand());

                // Re-anchoring moves the whole group, not just the joiner — which is correct: the
                // reload froze the clock, so every screen has to be told where the song now is.
                await PublishTimelineAsync(isPlaying: true, position, scheduleAhead: true);
            }
            else
            {
                await PlayAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to sync a newly connected screen");
        }
        finally
        {
            // Restarting here rather than beside the PlayCommand also covers the throwing path:
            // a stopped clock under a Playing state would freeze the UI for the rest of the song.
            if (State == PlaybackState.Playing && _timer is null)
                StartClock();

            _screenSyncLock.Release();
        }
    }

    private async Task HandleScreenLossAsync()
    {
        await _screenSyncLock.WaitAsync();
        try
        {
            if (State != PlaybackState.Playing)
                return;

            if (await HasConnectedScreenAsync())
                return;

            var behavior = await GetScreenDisconnectBehaviorAsync();

            Logger.LogWarning("Last screen disconnected mid-performance; applying {Behavior}", behavior);

            switch (behavior)
            {
                case ScreenDisconnectBehavior.CancelPerformance:
                    ResetState();
                    await EndedAsync();
                    InvokeStateChanged();
                    break;

                case ScreenDisconnectBehavior.RestartFromStart:
                    await PauseAsync();
                    Position = TimeSpan.Zero;
                    InvokeStateChanged();
                    break;

                default:
                    _resumeWhenScreenReturns = true;
                    await PauseAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to handle screen disconnect");
        }
        finally
        {
            _screenSyncLock.Release();
        }
    }

    private void StartClock()
    {
        _lastTick = DateTime.UtcNow;
        _timer?.Dispose();
        _timer = new Timer(OnTick, null, 500, 500);
    }

    private void StopClock()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task<ScreenDisconnectBehavior> GetScreenDisconnectBehaviorAsync()
    {
        var venue = await _venuesService.ReadSelectedVenueAsync();
        return venue?.Settings.OnScreenDisconnect ?? ScreenDisconnectBehavior.ResumeOnReconnect;
    }

    private void ResetState()
    {
        StopClock();

        _sessionActivity?.Dispose();
        _sessionActivity = null;

        CurrentlyPerformingUserId = null;
        _resumeWhenScreenReturns = false;

        State = PlaybackState.Stopped;
        StopFadeDuration = null;
        Position = TimeSpan.Zero;
    }

    /// <summary>
    /// Opens a host-side transcode and describes it for the screens. <c>FilePath</c> stays
    /// populated because KHost.Screen decodes locally and cannot consume the stream.
    /// </summary>
    private async Task<LoadMediaCommand> BuildLoadCommandAsync(Media media, TimeSpan startOffset)
    {
        await CloseStreamAsync();

        try
        {
            _stream = await _mediaStreams.OpenAsync(media.FilePath, startOffset);
        }
        catch (Exception ex)
        {
            // A screen sharing the host's filesystem can still play from FilePath, so a failed
            // transcode degrades the performance rather than ending it.
            Logger.LogError(ex, "Could not open a host stream for '{FilePath}'", media.FilePath);
        }

        return DescribeStream(media);
    }

    private LoadMediaCommand DescribeStream(Media media) => new()
    {
        FilePath = media.FilePath,
        StreamUrl = _stream?.PlaylistUrl,
        StreamStartOffset = _stream?.StartOffset ?? TimeSpan.Zero,
    };

    private async Task CloseStreamAsync()
    {
        var stream = _stream;
        _stream = null;

        if (stream is null) return;

        try { await _mediaStreams.CloseAsync(stream.Id); }
        catch (Exception ex) { Logger.LogWarning(ex, "Failed to close media stream {SessionId}", stream.Id); }
    }

    private async Task EndedAsync()
    {
        // Every path that finishes a performance runs through here, so it is the one place the
        // transcode has to stop — an orphaned ffmpeg would keep burning CPU for a song nobody plays.
        await CloseStreamAsync();

        var currentPerformance = CurrentPerformance;

        CurrentPerformance = null;
        CurrentMedia = null;

        _singerQueueService.UnlockTopSlot();

        if (currentPerformance is null)
            return;

        Logger.LogInformation("Performance {PerformanceId} ended for user {UserId}", currentPerformance.Id, currentPerformance.SingerId);

        // Dequeue first so rotation's songs-sung-tonight count includes the finished song.
        await _performanceService.DequeueAsync(currentPerformance.SingerId, currentPerformance.Id);
        await _singerQueueService.RotateQueueAsync(currentPerformance.SingerId);
    }

    private async Task SendToScreensAsync(IScreenCommand command)
    {
        try
        {
            await _screenServer.BroadcastCommandAsync(command);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send {Command} to screens", command.GetType().Name);
        }
    }

    /// <summary>
    /// Publishes where the song should be, against the host clock, to the screens that can hold
    /// themselves to it. A start is anchored slightly ahead so every synced screen begins on the
    /// same instant rather than on whenever its own command landed.
    /// </summary>
    private async Task PublishTimelineAsync(bool isPlaying, TimeSpan? position = null, bool scheduleAhead = false)
    {
        var anchor = DateTime.UtcNow + (scheduleAhead ? _options.SyncStartLead : TimeSpan.Zero);

        await PublishTimelineAsync(position ?? Position, anchor, isPlaying);
    }

    private async Task PublishTimelineAsync(TimeSpan position, DateTime anchorUtc, bool isPlaying)
    {
        try
        {
            var screens = await SyncCapableScreensAsync();
            if (screens.Count == 0) return;

            await _screenCoordination.EnsureRolesAsync();
            var primaryScreenId = _screenCoordination.PrimaryScreenId;

            // Addressed per screen rather than broadcast: a loose consumer cannot honour a
            // schedule, and sending it one would only invite it to try.
            foreach (var screen in screens)
                await _screenServer.SendCommandAsync(screen.ScreenId, new SetTimelineCommand
                {
                    Position = position,
                    AnchorUtc = anchorUtc,
                    IsPlaying = isPlaying,
                    IsPrimary = screen.ScreenId == primaryScreenId,
                });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to publish the timeline to synced screens");
        }
    }

    /// <summary>
    /// Re-anchors the group onto what the primary is actually playing. Without this the host holds
    /// the followers to a clock they cannot reach — an HLS seek lands on a segment boundary, so a
    /// screen settles behind the anchor and trims its rate forever to chase it. A permanent trim
    /// is a permanent pitch error, which a karaoke screen cannot have; anchoring on a position a
    /// real screen has actually reached lets the followers converge and return to true speed.
    /// </summary>
    private void OnScreenStateReceived(object? sender, ScreenStateReceivedEventArgs e)
    {
        if (State != PlaybackState.Playing) return;
        if (e.ScreenId != _screenCoordination.PrimaryScreenId) return;
        if (e.State is not ScreenPlaybackState state) return;
        if (!state.IsPlaying || state.SampledAtUtc is not { } sampledAt) return;

        // The primary defines the song position, so the host's own clock follows it rather than
        // free-running — the timer is only an interpolator between these reports now.
        Position = state.Position + (DateTime.UtcNow - sampledAt);
        _lastTick = DateTime.UtcNow;

        _ = PublishTimelineAsync(state.Position, sampledAt, isPlaying: true);
    }

    private async Task<List<IScreenConnection>> SyncCapableScreensAsync()
    {
        var screens = new List<IScreenConnection>();

        try
        {
            await foreach (var screen in _screenServer.GetConnectedScreensAsync())
                if (screen.Capabilities.SupportsSync)
                    screens.Add(screen);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to enumerate synced screens");
        }

        return screens;
    }

    private async void OnTick(object? state)
    {
        try
        {
            await TickAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unhandled error in playback tick");
        }
    }

    internal async Task TickAsync()
    {
        var now = DateTime.UtcNow;
        Position += now - _lastTick;
        _lastTick = now;

        if (HasPlaybackEnded())
        {
            ResetState();

            Logger.LogInformation("Playback concluded");

            await EndedAsync();
        }

        InvokeStateChanged();
    }

    private bool HasPlaybackEnded() =>
        CurrentMedia?.Duration is { } duration && Position >= duration;

    private static class AnalyticActivities
    {
        public const string Session = "playback.session";
    }
}
