using KHost.Abstractions.Models;
using System.Text.Json.Serialization;

namespace KHost.Abstractions.Services;

public interface IPerformanceService : IRepositoryService<Performance>
{
    Task<PaginatedResult<Performance>> ReadAllAsync(int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.Queued);
    Task<PaginatedResult<Performance>> ReadBySingerIdAsync(Guid singerId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued, DateTime? startDate = null);
    Task<PaginatedResult<Performance>> ReadByMediaIdAsync(Guid mediaId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued);

    Task<Performance?> ReadSingersNextPerformanceAsync(Guid singerId);
    Task<List<Performance>> ReadQueuedAsync();

    /// <summary>Null when the venue's duplicate-song warning was shown and declined.</summary>
    Task<Performance?> CreateAndEnqueueAsync(Performance performance);
    Task DequeueAsync(Guid singerId, Guid performanceId);
    Task MoveUpInQueueAsync(Guid singerId, Guid performanceId);
    Task MoveDownInQueueAsync(Guid singerId, Guid performanceId);
    Task MoveToEndOfQueueAsync(Guid singerId, Guid performanceId);
    Task DeleteAllQueuedAsync();
}
