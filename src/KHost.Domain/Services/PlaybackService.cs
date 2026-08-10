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
    }

    private Timer? _timer;
    private DateTime _lastTick;
    private IAnalyticsActivity? _sessionActivity;
    private readonly ISingerQueueService _singerQueueService;
    private readonly IPerformanceService _performanceService;
    private readonly IVenuesService _venuesService;
    private readonly IAnalyticsService _analytics;
    private readonly IScreenServer _screenServer;
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
        IOptions<ServiceOptions> options)
        : base(logger)
    {
        _singerQueueService = singerQueueService;
        _performanceService = performanceService;
        _venuesService = venuesService;
        _analytics = analytics;
        _screenServer = screenServer;
        _options = options.Value;

        _screenServer.ScreenDisconnected += OnScreenDisconnected;
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

        await SendToScreensAsync(new LoadMediaCommand { FilePath = media.FilePath });

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
        _lastTick = DateTime.UtcNow;
        _timer?.Dispose();
        _timer = new Timer(OnTick, null, 500, 500);

        _analytics.RecordPlaybackStateTransition(PlaybackState.Playing);

        Logger.LogInformation("Playback started for user {UserId}", CurrentPerformance.SingerId);

        await SendToScreensAsync(new PlayCommand());

        InvokeStateChanged();
    }

    public async Task PauseAsync()
    {
        if (State != PlaybackState.Playing)
            return;

        State = PlaybackState.Paused;
        _timer?.Dispose();
        _timer = null;

        _analytics.RecordPlaybackStateTransition(PlaybackState.Paused);

        Logger.LogInformation("Playback paused at {Position}", Position);

        await SendToScreensAsync(new PauseCommand());

        InvokeStateChanged();

        return;
    }

    public async Task StopAsync()
    {
        if (State == PlaybackState.Stopping)
            return;

        var fade = _options.StopFadeDuration;

        // Hold CurrentPerformance/CurrentMedia until the screens have finished fading, so the
        // UI can show the song winding down instead of blanking while it is still audible.
        _timer?.Dispose();
        _timer = null;
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
            _screenServer.ScreenDisconnected -= OnScreenDisconnected;

            _timer?.Dispose();
            _timer = null;
        }
    }

    // ScreenServerService raises this while holding its connection lock, and
    // GetConnectedScreensAsync waits on that same non-reentrant lock — so the check has to run
    // off this thread or it deadlocks the hub.
    private void OnScreenDisconnected(object? sender, ScreenConnectionEventArgs e) =>
        _ = Task.Run(PauseIfNoScreensRemainAsync);

    private async Task PauseIfNoScreensRemainAsync()
    {
        try
        {
            if (State != PlaybackState.Playing)
                return;

            if (await HasConnectedScreenAsync())
                return;

            Logger.LogWarning("Last screen disconnected mid-performance; pausing playback");

            await PauseAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to handle screen disconnect");
        }
    }

    private void ResetState()
    {
        _timer?.Dispose();
        _timer = null;

        _sessionActivity?.Dispose();
        _sessionActivity = null;

        CurrentlyPerformingUserId = null;

        State = PlaybackState.Stopped;
        StopFadeDuration = null;
        Position = TimeSpan.Zero;
    }

    private async Task EndedAsync()
    {
        var currentPerformance = CurrentPerformance;

        CurrentPerformance = null;
        CurrentMedia = null;

        _singerQueueService.UnlockTopSlot();

        if (currentPerformance is null)
            return;

        Logger.LogInformation("Performance {PerformanceId} ended for user {UserId}", currentPerformance.Id, currentPerformance.SingerId);

        await MoveQueueAfterPerformanceAsync(currentPerformance.SingerId);
        await _performanceService.DequeueAsync(currentPerformance.SingerId, currentPerformance.Id);
    }

    private async Task MoveQueueAfterPerformanceAsync(Guid singerId)
    {
        var venue = await _venuesService.ReadSelectedVenueAsync();
        if (venue?.Settings.MoveSingerToBottomAfterPerformance != true)
            return;

        await _singerQueueService.MoveUserToEndAsync(singerId);
        await _singerQueueService.SelectFirstUserInQueueAsync();
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
