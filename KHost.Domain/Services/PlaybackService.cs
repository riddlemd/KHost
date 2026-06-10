using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class PlaybackService : BaseService, IPlaybackService
{
    private Timer? _timer;
    private DateTime _lastTick;
    private IAnalyticsActivity? _sessionActivity;
    private readonly ISingerQueueService _singerQueueService;
    private readonly IPerformanceService _performanceService;
    private readonly IVenuesService _venuesService;
    private readonly IAnalyticsService _analytics;
    private readonly IScreenServer _screenServer;

    public Performance? CurrentPerformance { get; private set; }
    public Media? CurrentMedia { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    public TimeSpan Position { get; private set; }
    public Guid? CurrentlyPerformingUserId { get; private set; }

    public PlaybackService(
        ILogger<PlaybackService> logger,
        ISingerQueueService singerQueueService,
        IPerformanceService performanceService,
        IVenuesService venuesService,
        IAnalyticsService analytics,
        IScreenServer screenServer)
        : base(logger)
    {
        _singerQueueService = singerQueueService;
        _performanceService = performanceService;
        _venuesService = venuesService;
        _analytics = analytics;
        _screenServer = screenServer;
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

        CurrentlyPerformingUserId = CurrentPerformance.SingerId;

        State = PlaybackState.Playing;
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
        _analytics.RecordPlaybackStateTransition(PlaybackState.Stopped);

        ResetState();

        Logger.LogInformation("Playback stopped");

        await SendToScreensAsync(new StopCommand());

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
            _timer?.Dispose();
            _timer = null;
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

    private async Task TickAsync()
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
