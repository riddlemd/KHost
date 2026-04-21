using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IPerformancesRepository : IRepository<Performance>
{
    Task<int> GetNextQueuePositionForSingerAsync(Guid singerId);
    Task<Performance?> GetSingersNextPerformanceAsync(Guid singerId);
    Task<PaginatedResult<Performance>> GetBySingerIdAsync(Guid singerId, int pageNumber = 0, int pageSize = 0, bool includeQueued = false);
    Task<PaginatedResult<Performance>> GetByMediaIdAsync(Guid mediaId, int pageNumber = 0, int pageSize = 0, bool includeQueued = false);
}
