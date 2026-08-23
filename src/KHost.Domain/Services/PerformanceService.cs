using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class PerformanceService : BaseRepositoryService<Performance, IPerformancesRepository>, IPerformanceService
{
    private readonly IMediaService _mediaService;
    private readonly IVenuesService _venuesService;
    private readonly IInteractionDispatcher _interactions;
    private readonly IPluginImportCancellation _pluginImportCancellation;

    public PerformanceService(
        ILogger<PerformanceService> logger,
        IPerformancesRepository repository,
        IMediaService mediaService,
        IVenuesService venuesService,
        IInteractionDispatcher interactions,
        IPluginImportCancellation pluginImportCancellation)
        : base(logger, repository)
    {
        _mediaService = mediaService;
        _venuesService = venuesService;
        _interactions = interactions;
        _pluginImportCancellation = pluginImportCancellation;
    }

    // No gate service: removing a queued performance is what a dequeue IS, so killing an
    // in-flight download for it lives right here rather than behind a separate guard.
    public override async Task<bool> DeleteAsync(Guid id)
    {
        // Read before the row is gone — once it is deleted there is nothing left to look its
        // media id up from.
        var mediaId = (await Repository.ReadAsync(id))?.MediaId;

        var deleted = await base.DeleteAsync(id);

        if (deleted && mediaId is { } removedMediaId)
        {
            var media = await _mediaService.ReadAsync(removedMediaId);
            if (media?.Status == MediaStatus.Downloading)
                await _pluginImportCancellation.CancelImportAsync(removedMediaId);
        }

        return deleted;
    }

    public async Task<PaginatedResult<Performance>> ReadBySingerIdAsync(Guid singerId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.All, DateTime? startDate = null)
        => await Repository.ReadBySingerIdAsync(singerId, pageNumber, pageSize, filter, startDate);

    public async Task<PaginatedResult<Performance>> ReadByMediaIdAsync(Guid mediaId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.All)
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

    public async Task<PaginatedResult<Performance>> ReadAllAsync(int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.All)
        => await Repository.ReadAllAsync(pageNumber, pageSize, filter);

    public async Task<Performance?> CreateAndEnqueueAsync(Performance performance)
    {
        if (!await ConfirmNotADuplicateAsync(performance.MediaId))
        {
            Logger.LogInformation("Enqueue of media {MediaId} declined at the duplicate warning", performance.MediaId);
            return null;
        }

        var nextPosition = await Repository.ReadNextQueuePositionForSingerAsync(performance.SingerId);

        performance.QueuePosition = nextPosition;
        // Stamped at enqueue: the performance belongs to the venue it was sung at, so it must not
        // follow the host to whatever venue is selected when the history is read back.
        performance.VenueId ??= _venuesService.SelectedVenueId;

        await Repository.CreateAsync(performance);

        Logger.LogInformation("Enqueued media {MediaId} for singer {SingerId} at position {Position}", performance.MediaId, performance.SingerId, nextPosition);

        InvokeStateChanged();

        return performance;
    }

    private async Task<bool> ConfirmNotADuplicateAsync(Guid mediaId)
    {
        var settings = (await _venuesService.ReadSelectedVenueAsync())?.Settings;

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
        }
        else
        {
            Logger.LogWarning("Performance {PerformanceId} not found for singer {SingerId}", performanceId, singerId);
        }

        InvokeStateChanged();
    }

    public async Task DeleteAllQueuedAsync()
    {
        await Repository.DeleteAllQueuedAsync();

        Logger.LogInformation("All queued performances deleted");

        InvokeStateChanged();
    }



    public async Task MoveUpInQueueAsync(Guid singerId, Guid performanceId)
    {
        var queue = (await Repository.ReadQueuedAsync())
            .Where(p => p.SingerId == singerId)
            .ToList();

        var idx = queue.FindIndex(p => p.Id == performanceId);

        if (idx > 0)
        {
            var perf = queue[idx];
            var prevPerf = queue[idx - 1];
            (perf.QueuePosition, prevPerf.QueuePosition) = (prevPerf.QueuePosition, perf.QueuePosition);

            await Repository.UpdateAsync(perf);
            await Repository.UpdateAsync(prevPerf);

            Logger.LogDebug("Moved performance {PerformanceId} up from position {OldPosition} to {NewPosition}", performanceId, prevPerf.QueuePosition, perf.QueuePosition);

            InvokeStateChanged();
        }
    }

    public async Task MoveDownInQueueAsync(Guid singerId, Guid performanceId)
    {
        var queue = (await Repository.ReadQueuedAsync())
            .Where(p => p.SingerId == singerId)
            .ToList();

        var idx = queue.FindIndex(p => p.Id == performanceId);

        if (idx >= 0 && idx < queue.Count - 1)
        {
            var perf = queue[idx];
            var nextPerf = queue[idx + 1];
            (perf.QueuePosition, nextPerf.QueuePosition) = (nextPerf.QueuePosition, perf.QueuePosition);

            await Repository.UpdateAsync(perf);
            await Repository.UpdateAsync(nextPerf);

            Logger.LogDebug("Moved performance {PerformanceId} down from position {OldPosition} to {NewPosition}", performanceId, nextPerf.QueuePosition, perf.QueuePosition);

            InvokeStateChanged();
        }
    }

    /// <summary>Drop a song at an arbitrary position — what a drag ends in.</summary>
    public async Task MoveToIndexAsync(Guid singerId, Guid performanceId, int newIndex)
    {
        var queue = (await Repository.ReadQueuedAsync())
            .Where(p => p.SingerId == singerId)
            .ToList();

        var idx = queue.FindIndex(p => p.Id == performanceId);

        if (idx < 0) return;

        // A drag can land past either end of the singer's own list — the row it was dropped on
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

        InvokeStateChanged();
    }

    public async Task MoveToEndOfQueueAsync(Guid singerId, Guid performanceId)
    {
        var queue = (await Repository.ReadQueuedAsync())
            .Where(p => p.SingerId == singerId)
            .ToList();

        var idx = queue.FindIndex(p => p.Id == performanceId);

        if (idx >= 0 && idx < queue.Count - 1)
        {
            var perf = queue[idx];
            var maxPosition = queue.Max(p => p.QueuePosition) ?? 0;
            perf.QueuePosition = maxPosition + 1;

            await Repository.UpdateAsync(perf);

            Logger.LogDebug("Moved performance {PerformanceId} to end of queue at position {Position}", performanceId, perf.QueuePosition);

            InvokeStateChanged();
        }
    }
}
