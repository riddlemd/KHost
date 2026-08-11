using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IPerformancesRepository : IRepository<Performance>
{
    Task<int> ReadNextQueuePositionForSingerAsync(Guid singerId);
    Task<Performance?> ReadSingersNextPerformanceAsync(Guid singerId);
    Task<List<Performance>> ReadQueuedAsync();
    Task<PaginatedResult<Performance>> ReadAllAsync(int pageNumber = 0, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.Queued);
    Task<PaginatedResult<Performance>> ReadBySingerIdAsync(Guid singerId, int pageNumber = 0, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued, DateTime? startDate = null);
    Task<PaginatedResult<Performance>> ReadByMediaIdAsync(Guid mediaId, int pageNumber = 0, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued);
    Task DeleteAllQueuedAsync();
}
