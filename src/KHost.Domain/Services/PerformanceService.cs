using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class PerformanceService : BaseRepositoryService<Performance, IPerformancesRepository>, IPerformanceService
{
    public PerformanceService(ILogger<PerformanceService> logger, IPerformancesRepository repository)
        : base(logger, repository)
    {
        //
    }

    public async Task<PaginatedResult<Performance>> ReadBySingerIdAsync(Guid singerId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.All)
        => await Repository.ReadBySingerIdAsync(singerId, pageNumber, pageSize, filter);

    public async Task<PaginatedResult<Performance>> ReadByMediaIdAsync(Guid mediaId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.All)
        => await Repository.ReadByMediaIdAsync(mediaId, pageNumber, pageSize, filter);

    public async Task<Performance?> ReadSingersNextPerformanceAsync(Guid singerId)
        => await Repository.ReadSingersNextPerformanceAsync(singerId);

    public async Task<List<Performance>> ReadQueuedAsync()
        => await Repository.ReadQueuedAsync();

    public async Task<PaginatedResult<Performance>> ReadAllAsync(int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.All)
        => await Repository.ReadAllAsync(pageNumber, pageSize, filter);

    public async Task<Performance> CreateAndEnqueueAsync(Performance performance)
    {
        var nextPosition = await Repository.ReadNextQueuePositionForSingerAsync(performance.SingerId);

        performance.QueuePosition = nextPosition;

        await Repository.CreateAsync(performance);

        Logger.LogInformation("Enqueued media {MediaId} for singer {SingerId} at position {Position}", performance.MediaId, performance.SingerId, nextPosition);

        InvokeStateChanged();

        return performance;
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
