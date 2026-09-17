using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IPerformancesRepository : IRepository<Performance>
{
    Task<int> ReadNextQueuePositionForSingerAsync(Guid singerId);
    Task<Performance?> ReadSingersNextPerformanceAsync(Guid singerId);
    Task<List<Performance>> ReadQueuedAsync();
    Task<PaginatedResult<Performance>> ReadAllAsync(int pageNumber = 0, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.Queued);
    Task<PaginatedResult<Performance>> ReadBySingerIdAsync(Guid singerId, int pageNumber = 0, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued, DateTime? startDate = null);

    /// <summary>Sung counts since <paramref name="since"/>; nothing sung is absent, not zero.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountSungSinceAsync(IEnumerable<Guid> singerIds, DateTime since);

    /// <summary>Distinct venues sung at, most recent first; pre-tracking performances skipped.</summary>
    Task<IReadOnlyList<RecentVenueVisit>> ReadRecentVenueVisitsBySingerAsync(Guid singerId, int count);

    /// <summary>Most recent venue per singer; one with none recorded is absent, not null.</summary>
    Task<IReadOnlyDictionary<Guid, RecentVenueVisit>> ReadLastVenueBySingersAsync(IEnumerable<Guid> singerIds);
    Task<PaginatedResult<Performance>> ReadByMediaIdAsync(Guid mediaId, int pageNumber = 0, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued);
    Task DeleteAllQueuedAsync();
}
