using KHost.Domain.Services.MediaLifetime;
using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using KHost.Common.Media;
using KHost.Common.Visualisations;

namespace KHost.Domain.Services;

public class PerformanceService : BaseRepositoryService<Performance, IPerformancesRepository>, IPerformanceService
{
    // Resolved on use, never in the constructor: a gate is a plugin, and a plugin takes this
    // service, so asking for one up front closes a ring the container cannot build.
    private readonly IServiceProvider _services;
    private readonly IMediaService _mediaService;
    private readonly IUsersService _usersService;
    private readonly IVenuesService _venuesService;
    private readonly IInteractionDispatcher _interactions;
    private readonly IDownloadsService _downloadsService;
    private readonly SemaphoreSlim _enqueueLock = new(1, 1);

    public PerformanceService(
        ILogger<PerformanceService> logger,
        IPerformancesRepository repository,
        IMediaService mediaService,
        IUsersService usersService,
        IVenuesService venuesService,
        IInteractionDispatcher interactions,
        IDownloadsService downloadsService,
        IServiceProvider services,
        IMessageBroker broker)
        : base(logger, repository, broker, new PerformancesChanged())
    {
        _services = services;
        _mediaService = mediaService;
        _usersService = usersService;
        _venuesService = venuesService;
        _interactions = interactions;
        _downloadsService = downloadsService;
    }

    // No gate service: removing a queued performance is what a dequeue IS, so killing an
    // in-flight download for it lives right here rather than behind a separate guard.
    public override async Task<bool> DeleteAsync(Guid id)
    {
        // Read before the row is gone: once it is deleted there is nothing left to look its
        // media id up from.
        var mediaId = (await Repository.ReadAsync(id))?.MediaId;

        var deleted = await base.DeleteAsync(id);

        // Another turn may still be waiting on the same download: two guests' picks of a song that
        // is still arriving share one row.
        if (deleted && mediaId is { } removedMediaId
            && !(await ReadQueuedAsync()).Any(p => p.MediaId == removedMediaId))
        {
            var media = await _mediaService.ReadAsync(removedMediaId);
            if (media?.Status.IsAcquiring() == true)
                await _downloadsService.CancelAsync(removedMediaId);
        }

        return deleted;
    }

    public async Task<PaginatedResult<Performance>> ReadBySingerIdAsync(Guid singerId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued, DateTime? startDate = null)
        => await Repository.ReadBySingerIdAsync(singerId, pageNumber, pageSize, filter, startDate);

    public async Task<PaginatedResult<Performance>> ReadByMediaIdAsync(Guid mediaId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued)
        => await Repository.ReadByMediaIdAsync(mediaId, pageNumber, pageSize, filter);

    public async Task<IReadOnlyDictionary<Guid, int>> CountSungSinceAsync(IEnumerable<Guid> singerIds, DateTime since)
        => await Repository.CountSungSinceAsync(singerIds, since);

    public async Task<IReadOnlyList<RecentVenueVisit>> ReadRecentVenueVisitsBySingerAsync(Guid singerId, int count)
        => await Repository.ReadRecentVenueVisitsBySingerAsync(singerId, count);

    public async Task<IReadOnlyDictionary<Guid, RecentVenueVisit>> ReadLastVenueBySingersAsync(IEnumerable<Guid> singerIds)
        => await Repository.ReadLastVenueBySingersAsync(singerIds);

    public async Task<Performance?> ReadSingersNextPerformanceAsync(Guid singerId)
        => await Repository.ReadSingersNextPerformanceAsync(singerId);

    public async Task<List<Performance>> ReadQueuedAsync()
        => await Repository.ReadQueuedAsync();

    public async Task<PaginatedResult<Performance>> ReadAllAsync(int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.Queued)
        => await Repository.ReadAllAsync(pageNumber, pageSize, filter);

    public async Task<Performance?> CreateAndEnqueueAsync(Performance performance)
        => (await TryCreateAndEnqueueAsync(performance)).Performance;

    public Task<EnqueueResult> TryCreateAndEnqueueAsync(Performance performance)
        => TryCreateAndEnqueueAsync(performance, EnqueueOrigin.Host);

    public async Task<EnqueueResult> TryCreateAndEnqueueAsync(Performance performance, EnqueueOrigin origin)
    {
        var settings = (await _venuesService.ReadSelectedVenueAsync())?.Settings;

        // Before the duplicate-song warning: a remote's double tap must not put a dialog in front of
        // the host. The plugin that sent it reads the conflict off the result to tell the singer.
        if (await RefusalByTheQueueAsync(performance, settings, origin) is { } conflict)
            return conflict;

        if (!await ConfirmNotADuplicateAsync(performance.MediaId, settings))
        {
            Logger.LogInformation("Enqueue of media {MediaId} declined at the duplicate warning", performance.MediaId);
            return new EnqueueResult(EnqueueResultType.DeclinedAtWarning);
        }

        // Refused at the queue, not only at the microphone. A song whose provider will not let it
        // play is one nobody can sing, and a host finds that out now rather than in front of a room
        // with the singer already up.
        if (await RefusedByItsProviderAsync(performance.MediaId) is { } refusal)
        {
            Logger.LogInformation("Enqueue of media {MediaId} refused: {Reason}", performance.MediaId, refusal);
            return new EnqueueResult(EnqueueResultType.RefusedByProvider, Reason: refusal);
        }

        // A song whose file the host removed is fetched again here, so the turn queues behind a
        // download exactly as a fresh pick from its provider does.
        if (await RefusedForItsMissingFileAsync(performance.MediaId) is { } missing)
        {
            Logger.LogInformation("Enqueue of media {MediaId} refused: {Reason}", performance.MediaId, missing);
            return new EnqueueResult(EnqueueResultType.RefusedByProvider, Reason: missing);
        }

        // Filled here, not by each of the five callers (two in plugins): a line each is what goes missing.
        // A caller with its own name to record (a remote nickname) has already set it; this leaves it.
        if (string.IsNullOrWhiteSpace(performance.SungAs))
            performance.SungAs = (await _usersService.ReadAsync(performance.SingerId))?.Name;

        // Stamped at enqueue: the performance belongs to the venue it was sung at, so it must not
        // follow the host to whatever venue is selected when the history is read back.
        performance.VenueId ??= _venuesService.SelectedVenueId;

        performance.Background = VisualisationLooks.BackgroundWithinRanges(performance.Background);

        // Here rather than left to each caller: history sorts on this, so an unstamped row sinks
        // below every real one and the singer's newest performance is the one they cannot find.
        if (performance.CreatedDate == default)
            performance.CreatedDate = DateTime.UtcNow;

        await _enqueueLock.WaitAsync();
        try
        {
            // Asked again under the lock: two taps arriving together both pass the first check while
            // the gate is awaited, and the lock is not held there because the warning waits on a person.
            if (await RefusalByTheQueueAsync(performance, settings, origin) is { } lateConflict)
                return lateConflict;

            performance.QueuePosition = await Repository.ReadNextQueuePositionForSingerAsync(performance.SingerId);

            await Repository.CreateAsync(performance);
        }
        finally
        {
            _enqueueLock.Release();
        }

        Logger.LogInformation("Enqueued media {MediaId} for singer {SingerId} at position {Position}", performance.MediaId, performance.SingerId, performance.QueuePosition);

        AnnounceChange();

        return new EnqueueResult(EnqueueResultType.Queued, performance);
    }

    /// <summary>Why a song whose file was removed cannot be queued, or null when it has its file or
    /// its provider has started fetching it again.</summary>
    private async Task<string?> RefusedForItsMissingFileAsync(Guid mediaId)
    {
        if (await _mediaService.ReadAsync(mediaId) is not { Status: MediaStatus.NotDownloaded } media)
            return null;

        if (_services.GetService<IMediaLifetimeService>() is { } lifetime && await lifetime.RefetchAsync(media))
            return null;

        var from = string.IsNullOrWhiteSpace(media.Source) ? "the plugin that made it" : media.Source;
        return $"'{media.Title}' needs downloading again, and {from} cannot do it: it may need installing, enabling or updating.";
    }

    /// <summary>The reason a provider will not let this song play, or null when it will.</summary>
    /// <remarks>The same gate playback asks. Licensed content with no live account is the case:
    /// without this a host queues it, waits for a render that is also refused, and learns nothing
    /// until the singer is standing there.</remarks>
    private async Task<string?> RefusedByItsProviderAsync(Guid mediaId)
    {
        try
        {
            if (_services.GetService<IMediaGateService>() is not { } gates)
                return null;

            if (await _mediaService.ReadAsync(mediaId) is not { } media)
                return null;

            var verdict = await gates.EvaluateAsync(MediaAction.Queue, media);
            if (verdict.Allowed)
                return null;

            var reason = string.IsNullOrWhiteSpace(verdict.Reason)
                ? "That song cannot be played right now."
                : verdict.Reason;

            _services.GetService<IFlashService>()?.Show(reason, FlashType.Warning);

            return reason;
        }
        catch (Exception ex)
        {
            // A gate that throws must not stop a host queueing the rest of the night.
            Logger.LogWarning(ex, "Could not ask a provider about media {MediaId}", mediaId);
            return null;
        }
    }

    /// <summary>The refusal a song already in the queue earns, or null when nothing queued stands in
    /// its way.</summary>
    private async Task<EnqueueResult?> RefusalByTheQueueAsync(Performance performance, Venue.VenueSettings? settings, EnqueueOrigin origin)
    {
        var queued = await ReadQueuedAsync();
        var holders = queued.Where(p => p.MediaId == performance.MediaId).ToList();

        if (holders.FirstOrDefault(p => p.SingerId == performance.SingerId) is { } own)
        {
            Logger.LogInformation("Enqueue of media {MediaId} refused: singer {SingerId} already has it queued", performance.MediaId, performance.SingerId);
            return new EnqueueResult(EnqueueResultType.AlreadyQueued, Conflict: own);
        }

        if (settings?.RefuseSongQueuedForAnotherSinger == true && holders.FirstOrDefault() is { } other)
        {
            Logger.LogInformation("Enqueue of media {MediaId} for singer {SingerId} refused: singer {HolderId} has it queued", performance.MediaId, performance.SingerId, other.SingerId);
            return new EnqueueResult(EnqueueResultType.QueuedForAnotherSinger, Conflict: other);
        }

        if (origin == EnqueueOrigin.Remote
            && settings?.RemoteSongLimit is > 0 and var limit
            && queued.Count(p => p.SingerId == performance.SingerId) >= limit)
        {
            Logger.LogInformation("Remote enqueue of media {MediaId} refused: singer {SingerId} has the venue's limit of {Limit} queued", performance.MediaId, performance.SingerId, limit);
            return new EnqueueResult(EnqueueResultType.SingerAtLimit);
        }

        return null;
    }

    private async Task<bool> ConfirmNotADuplicateAsync(Guid mediaId, Venue.VenueSettings? settings)
    {
        if (settings?.WarnOnDuplicateSong != true)
            return true;

        var timesQueued = (await ReadQueuedAsync()).Count(p => p.MediaId == mediaId);
        var sungWithinHours = await GetHoursSinceLastSungAsync(mediaId, settings.DuplicateSongWindowHours);

        if (timesQueued == 0 && sungWithinHours is null)
            return true;

        var title = (await _mediaService.ReadAsync(mediaId))?.Title ?? "This song";

        return await _interactions.RequestAsync(new ConfirmDuplicateSongRequest(title, timesQueued, sungWithinHours));
    }

    // Performance only records when it was queued, not when it was sung, so this is the closest
    // signal available without adding a column.
    private async Task<int?> GetHoursSinceLastSungAsync(Guid mediaId, int windowHours)
    {
        if (windowHours <= 0)
            return null;

        var sung = await ReadByMediaIdAsync(mediaId, filter: PerformanceFilter.UnQueued);

        var mostRecent = sung.Items.Select(p => p.CreatedDate).DefaultIfEmpty().Max();

        if (mostRecent == default)
            return null;

        // A local clock here shifts every gap by the host's offset, silently moving the
        // duplicate-song window with the timezone.
        var elapsed = DateTime.UtcNow - mostRecent;

        return elapsed <= TimeSpan.FromHours(windowHours)
            ? (int)Math.Floor(Math.Max(elapsed.TotalHours, 0))
            : null;
    }

    public async Task DequeueAsync(Guid singerId, Guid performanceId)
    {
        var performance = await Repository.ReadAsync(performanceId);

        if (performance?.SingerId == singerId)
        {
            performance.QueuePosition = null;

            await Repository.UpdateAsync(performance);

            Logger.LogInformation("Dequeued performance {PerformanceId} for singer {SingerId}", performanceId, singerId);

            await RemoveSingleUseFileIfDoneAsync(performance.MediaId);
        }
        else
        {
            Logger.LogWarning("Performance {PerformanceId} not found for singer {SingerId}", performanceId, singerId);
        }

        AnnounceChange();
    }

    // A failure here costs only the cleanup; the turn has already moved into history.
    private async Task RemoveSingleUseFileIfDoneAsync(Guid mediaId)
    {
        try
        {
            if (_services.GetService<IMediaLifetimeService>() is not { } lifetime) return;

            var queued = (await ReadQueuedAsync()).Select(p => p.MediaId).ToHashSet();
            await lifetime.RemoveSingleUseFileIfDoneAsync(mediaId, queued);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not remove the file of single-use media {MediaId}", mediaId);
        }
    }

    public async Task<Performance?> UpdateSettingsAsync(Guid performanceId, PerformanceSettings settings)
    {
        if (await Repository.ReadAsync(performanceId) is not { } performance)
        {
            Logger.LogWarning("Performance {PerformanceId} not found; settings left unsaved", performanceId);
            return null;
        }

        // The ranges PlaybackService holds a live change to, so a saved value is one it can play.
        performance.Pitch = Math.Clamp(settings.Pitch, IPlaybackService.MinPitch, IPlaybackService.MaxPitch);
        performance.Tempo = Math.Clamp(settings.Tempo, IPlaybackService.MinTempo, IPlaybackService.MaxTempo);
        performance.LeadVolume = AudioLevels.ClampVolume(settings.LeadVolume);
        performance.BackingVolume = settings.BackingVolume is { } backing ? AudioLevels.ClampVolume(backing) : null;

        // Merged, as playback merges: a voice the editor never saw keeps the level it was sung at.
        if (settings.VoiceVolumes is { Count: > 0 } edited)
        {
            var voices = performance.VoiceVolumes is null ? [] : new Dictionary<string, int>(performance.VoiceVolumes);
            foreach (var (voice, level) in edited) voices[voice] = AudioLevels.ClampVolume(level);
            performance.VoiceVolumes = voices;
        }

        await Repository.UpdateAsync(performance);

        Logger.LogInformation("Settings saved on performance {PerformanceId}: key {Pitch:+#;-#;0}, tempo {Tempo:+#;-#;0}%",
            performanceId, performance.Pitch, performance.Tempo);

        AnnounceChange();

        return performance;
    }

    public async Task<Performance?> UpdateBackgroundAsync(Guid performanceId, PerformanceBackground? background)
    {
        if (await Repository.ReadAsync(performanceId) is not { } performance)
        {
            Logger.LogWarning("Performance {PerformanceId} not found; background left unsaved", performanceId);
            return null;
        }

        // A sung turn is history: nothing will draw it again, and a re-queue copies what it was.
        if (performance.QueuePosition is null)
        {
            Logger.LogWarning("Performance {PerformanceId} is not queued; background left unsaved", performanceId);
            return null;
        }

        performance.Background = VisualisationLooks.BackgroundWithinRanges(background);

        await Repository.UpdateAsync(performance);

        Logger.LogInformation("Background saved on performance {PerformanceId}: {Background}",
            performanceId, performance.Background?.Type.ToString() ?? "the venue's playlist");

        AnnounceChange();

        return performance;
    }

    public async Task DeleteAllQueuedAsync()
    {
        await Repository.DeleteAllQueuedAsync();

        Logger.LogInformation("All queued performances deleted");

        AnnounceChange();
    }

    public async Task MoveUpInQueueAsync(Guid singerId, Guid performanceId)
    {
        var queue = await ReadSingerQueueAsync(singerId);
        var idx = queue.FindIndex(p => p.Id == performanceId);

        if (idx > 0)
            await MoveToIndexAsync(singerId, performanceId, idx - 1);
    }

    public async Task MoveDownInQueueAsync(Guid singerId, Guid performanceId)
    {
        var queue = await ReadSingerQueueAsync(singerId);
        var idx = queue.FindIndex(p => p.Id == performanceId);

        if (idx >= 0)
            await MoveToIndexAsync(singerId, performanceId, idx + 1);
    }

    /// <summary>Drop a song at an arbitrary position, which is what a drag ends in.</summary>
    public async Task MoveToIndexAsync(Guid singerId, Guid performanceId, int newIndex)
    {
        var queue = await ReadSingerQueueAsync(singerId);

        var idx = queue.FindIndex(p => p.Id == performanceId);

        if (idx < 0) return;

        // A drag can land past either end of the singer's own list. The row it was dropped on
        // belongs to the whole table, and a queue can shrink while a drag is in flight.
        var target = Math.Clamp(newIndex, 0, queue.Count - 1);

        if (target == idx) return;

        var moved = queue[idx];
        queue.RemoveAt(idx);
        queue.Insert(target, moved);

        // Renumbered rather than swapped: a move across several rows shifts every position between
        // the two ends, and the pairwise swap the arrows use cannot express that.
        for (var position = 0; position < queue.Count; position++)
        {
            if (queue[position].QueuePosition == position + 1) continue;

            queue[position].QueuePosition = position + 1;
            await Repository.UpdateAsync(queue[position]);
        }

        Logger.LogDebug("Moved performance {PerformanceId} from index {OldIndex} to {NewIndex}", performanceId, idx, target);

        AnnounceChange();
    }

    /// <summary>The performances a singer has waiting, in queue order.</summary>
    private async Task<List<Performance>> ReadSingerQueueAsync(Guid singerId)
        => (await Repository.ReadQueuedAsync())
            .Where(p => p.SingerId == singerId)
            .ToList();
}
