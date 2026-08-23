using KHost.Abstractions.Models;
using System.Text.Json.Serialization;

namespace KHost.Abstractions.Services;

public interface IPerformanceService : IRepositoryService<Performance>
{
    Task<PaginatedResult<Performance>> ReadAllAsync(int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.Queued);
    Task<PaginatedResult<Performance>> ReadBySingerIdAsync(Guid singerId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued, DateTime? startDate = null);

    /// <summary>
    /// Sung counts per singer since <paramref name="since"/>, keyed by singer id. Singers with
    /// nothing since then are absent from the result rather than present with a zero.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountSungSinceAsync(IEnumerable<Guid> singerIds, DateTime since);

    /// <summary>
    /// Distinct venues the singer has sung at, most recently visited first. Performances recorded
    /// before venue tracking carry no venue and are skipped.
    /// </summary>
    Task<IReadOnlyList<RecentVenueVisit>> ReadRecentVenueVisitsBySingerAsync(Guid singerId, int count);

    Task<IReadOnlyDictionary<Guid, RecentVenueVisit>> ReadLastVenueBySingersAsync(IEnumerable<Guid> singerIds);
    Task<PaginatedResult<Performance>> ReadByMediaIdAsync(Guid mediaId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued);

    Task<Performance?> ReadSingersNextPerformanceAsync(Guid singerId);
    Task<List<Performance>> ReadQueuedAsync();

    /// <summary>Null when the venue's duplicate-song warning was shown and declined.</summary>
    Task<Performance?> CreateAndEnqueueAsync(Performance performance);
    Task DequeueAsync(Guid singerId, Guid performanceId);
    Task MoveUpInQueueAsync(Guid singerId, Guid performanceId);
    Task MoveDownInQueueAsync(Guid singerId, Guid performanceId);
    Task MoveToEndOfQueueAsync(Guid singerId, Guid performanceId);
    Task MoveToIndexAsync(Guid singerId, Guid performanceId, int newIndex);
    Task DeleteAllQueuedAsync();
}
