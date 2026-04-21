using KHost.Abstractions.Models;
using System.Text.Json.Serialization;

namespace KHost.Abstractions.Services;

public interface IPerformanceService : IRepositoryService<Performance>
{
    Task<PaginatedResult<Performance>> GetBySingerIdAsync(Guid singerId, int pageNumber = 1, int pageSize = 0, bool includeQueued = false);
    Task<PaginatedResult<Performance>> GetByMediaIdAsync(Guid mediaId, int pageNumber = 1, int pageSize = 0, bool includeQueued = false);

    Task<IReadOnlyList<Performance>> GetSingerPerformances(Guid singerId, int pageNumber = 0, int pageSize = 0);
    Task<Performance?> GetSingersNextPerformanceAsync(Guid singerId);

    Task<Performance> CreateAndEnqueueAsync(Performance performance);
    Task DequeueAsync(Guid singerId, Guid performanceId);
    Task MoveUpInQueueAsync(Guid singerId, Guid performanceId);
    Task MoveDownInQueueAsync(Guid singerId, Guid performanceId);
    Task MoveToEndOfQueueAsync(Guid singerId, Guid performanceId);
}
