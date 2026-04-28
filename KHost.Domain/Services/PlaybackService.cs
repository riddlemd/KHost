using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class PlaybackService : BaseService, IPlaybackService
{
    private Timer? _timer;
    private DateTime _lastTick;
    private readonly ISingerQueueService _singerQueueService;
    private readonly IPerformanceService _performanceService;
    private readonly IVenuesService _venuesService;

    public Performance? CurrentPerformance { get; private set; }
    public Media? CurrentMedia { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    public TimeSpan Position { get; private set; }
    public Guid? CurrentlyPerformingUserId { get; private set; }

    public PlaybackService(
        ILogger<PlaybackService> logger,
        ISingerQueueService singerQueueService,
        IPerformanceService performanceService,
        IVenuesService venuesService)
        : base(logger)
    {
        _singerQueueService = singerQueueService;
        _performanceService = performanceService;
        _venuesService = venuesService;
    }

    public async Task LoadAsync(Performance performance, Media media)
    {
        ResetState();

        CurrentPerformance = performance;
        CurrentMedia = media;
        Position = TimeSpan.Zero;

        await _singerQueueService.MoveUserToStartAsync(performance.SingerId);
        _singerQueueService.LockTopSlot();

        Logger.LogInformation("Loading media '{Title}' for performance {PerformanceId}", media.Title, performance.Id);

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

        Logger.LogInformation("Playback started for user {UserId}", CurrentPerformance.SingerId);

        InvokeStateChanged();
    }

    public async Task PauseAsync()
    {
        if (State != PlaybackState.Playing)
            return;

        State = PlaybackState.Paused;
        _timer?.Dispose();
        _timer = null;

        Logger.LogInformation("Playback paused at {Position}", Position);

        InvokeStateChanged();

        return;
    }

    public async Task StopAsync()
    {
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
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void ResetState()
    {
        _timer?.Dispose();
        _timer = null;

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

        var venue = await _venuesService.ReadSelectedVenueAsync();
        if (venue?.Settings.MoveSingerToBottomAfterPerformance == true)
        {
            await _singerQueueService.MoveUserToEndAsync(currentPerformance.SingerId);
            await _singerQueueService.SelectFirstUserInQueueAsync();
        }

        await _performanceService.DequeueAsync(currentPerformance.SingerId, currentPerformance.Id);
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

        if (CurrentMedia?.Duration is { } duration && Position >= duration)
        {
            ResetState();

            Logger.LogInformation("Playback concluded");

            await EndedAsync();
        }

        InvokeStateChanged();
    }

}
