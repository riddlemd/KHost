using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Ads;

public class AdService : BaseService, IAdService, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IPlaybackService _playback;
    private readonly IMediaPoolService _pools;
    private readonly IMediaService _media;
    private readonly IVenuesService _venues;
    private readonly ISingerQueueService _queue;
    private readonly TimeProvider _time;

    public AdService(
        ILogger<AdService> logger,
        IPlaybackService playback,
        IMediaPoolService pools,
        IMediaService media,
        IVenuesService venues,
        ISingerQueueService queue,
        TimeProvider time)
        : base(logger)
    {
        _playback = playback;
        _pools = pools;
        _media = media;
        _venues = venues;
        _queue = queue;
        _time = time;

        _playback.PerformanceEnded += OnPerformanceEnded;
    }

    public bool IsConfigured { get; private set; }
    public int PerformancesSinceLastAd { get; private set; }
    public DateTimeOffset? LastAdAtUtc { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var venue = await _venues.ReadSelectedVenueAsync();

        IsConfigured = venue?.Settings.AdPoolId is not null;

        // Stamped at startup so every-N-minutes counts from opening rather than firing an ad into
        // the first gap of the night.
        LastAdAtUtc = _time.GetUtcNow();

        InvokeStateChanged();
    }

    public async Task<bool> PlayNowAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var (venue, pool) = await ReadActiveAsync();

            if (venue is null || pool is null)
            {
                Logger.LogInformation("No ad played: the venue has no ad playlist chosen");
                return false;
            }

            return await PlayFromAsync(venue, pool);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _playback.PerformanceEnded -= OnPerformanceEnded;
        _lock.Dispose();

        GC.SuppressFinalize(this);
    }

    // Registered as the gap's work rather than awaited here: the event is void, and playback holds
    // break music down until whatever fills the gap has started.
    private void OnPerformanceEnded(object? sender, PerformanceEndedEventArgs e)
        => e.Fill(HandlePerformanceEndedAsync());

    internal async Task HandlePerformanceEndedAsync()
    {
        await _lock.WaitAsync();
        try
        {
            PerformancesSinceLastAd++;

            var (venue, pool) = await ReadActiveAsync();

            if (venue is null || pool is null)
            {
                IsConfigured = false;
                InvokeStateChanged();
                return;
            }

            IsConfigured = true;

            if (!IsDue(pool))
            {
                InvokeStateChanged();
                return;
            }

            await PlayFromAsync(venue, pool);
        }
        catch (Exception ex)
        {
            // Never fatal: a broken ad must not stop the queue moving on to the next singer.
            Logger.LogError(ex, "Failed to play an ad after a performance");
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsDue(MediaPool pool) => pool.AdTrigger switch
    {
        AdTriggerMode.EveryNPerformances => PerformancesSinceLastAd >= Interval(pool),
        AdTriggerMode.EveryNMinutes => LastAdAtUtc is null
            || _time.GetUtcNow() - LastAdAtUtc.Value >= TimeSpan.FromMinutes(Interval(pool)),

        // Nobody left to sing, so the room gets the ad rather than an empty screen.
        AdTriggerMode.OnIdle => _queue.Users.Count == 0,

        _ => false,
    };

    // A hand-edited zero would fire an ad after every single song, or on every tick of the clock.
    private static int Interval(MediaPool pool) => Math.Max(pool.AdTriggerInterval, 1);

    private async Task<(Venue? Venue, MediaPool? Pool)> ReadActiveAsync()
    {
        var venue = await _venues.ReadSelectedVenueAsync();

        if (venue?.Settings.AdPoolId is not { } poolId)
            return (venue, null);

        return (venue, await _pools.ReadWithEntriesAsync(poolId));
    }

    /// <summary>Caller holds the lock.</summary>
    private async Task<bool> PlayFromAsync(Venue venue, MediaPool pool)
    {
        var mediaId = await _pools.SelectNextAsync(pool.Id, venue.Id);

        if (mediaId is null)
        {
            Logger.LogInformation("No ad played: playlist {PoolId} holds nothing playable", pool.Id);
            return false;
        }

        var media = await _media.ReadAsync(mediaId.Value);

        if (media is null)
        {
            Logger.LogWarning("No ad played: media {MediaId} is missing", mediaId);
            return false;
        }

        // Counters move only on an ad that actually reached the screen. A refusal — no screen, a
        // broken file — leaves it due so the next gap tries again rather than skipping the slot.
        if (!await _playback.PlayAdAsync(media))
            return false;

        PerformancesSinceLastAd = 0;
        LastAdAtUtc = _time.GetUtcNow();

        Logger.LogInformation("Ad '{Title}' playing from playlist {PoolId}", media.Title, pool.Id);

        InvokeStateChanged();

        return true;
    }
}
