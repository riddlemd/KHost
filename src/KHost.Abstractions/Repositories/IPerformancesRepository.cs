using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IPerformancesRepository : IRepository<Performance>
{
    Task<int> ReadNextQueuePositionForSingerAsync(Guid singerId);
    Task<Performance?> ReadSingersNextPerformanceAsync(Guid singerId);
    Task<List<Performance>> ReadQueuedAsync();
    Task<PaginatedResult<Performance>> ReadAllAsync(int pageNumber = 0, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.Queued);
    Task<PaginatedResult<Performance>> ReadBySingerIdAsync(Guid singerId, int pageNumber = 0, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued, DateTime? startDate = null);

    /// <summary>
    /// Sung counts per singer since <paramref name="since"/>, keyed by singer id. Singers with
    /// nothing since then are absent from the result rather than present with a zero.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountSungSinceAsync(IEnumerable<Guid> singerIds, DateTime since);
    Task<PaginatedResult<Performance>> ReadByMediaIdAsync(Guid mediaId, int pageNumber = 0, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued);
    Task DeleteAllQueuedAsync();
}
