using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

/// <summary>Performances: a singer's turn with one song, queued or already sung.</summary>
/// <remarks>
/// <para>A performance with a <see cref="Performance.QueuePosition"/> is queued; one without has
/// been sung (<see cref="PerformanceFilter.UnQueued"/>). The position orders one singer's own
/// list, not the room's rotation.</para>
/// <para>Host-implemented. A plugin goes through <see cref="Services.IPerformanceService"/>: it
/// announces <see cref="Messaging.Messages.PerformancesChanged"/>, and its
/// <see cref="Services.IPerformanceService.CreateAndEnqueueAsync"/> is where the enqueue rules run
/// (the duplicate-song warning, the provider's playback gate) and where the queue position, venue,
/// sung-as name and creation time are stamped. Creating a row here skips every one of them.</para>
/// <para>A performance holds only ids and dates, so text search always returns an empty page. Sort
/// keys: <c>createdDate</c> (default, newest first), <c>queuePosition</c>. Times are UTC.</para>
/// </remarks>
public interface IPerformancesRepository : IRepository<Performance>
{
    /// <summary>One past the singer's highest queue position; 1 when they have nothing queued.</summary>
    Task<int> ReadNextQueuePositionForSingerAsync(Guid singerId);

    /// <summary>The singer's queued performance with the lowest position, or null if none is queued.</summary>
    Task<Performance?> ReadSingersNextPerformanceAsync(Guid singerId);

    /// <summary>Every queued performance, unpaged, ordered by queue position.</summary>
    Task<List<Performance>> ReadQueuedAsync();

    /// <summary>One page of performances matching <paramref name="filter"/>, ordered by queue
    /// position (sung performances, having none, come first).</summary>
    Task<PaginatedResult<Performance>> ReadAllAsync(int pageNumber = 0, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.Queued);

    /// <summary>One page of a singer's performances, newest first.</summary>
    /// <param name="singerId">The singer whose performances are read.</param>
    /// <param name="pageNumber">1-based page to read; below 1 reads page 1.</param>
    /// <param name="pageSize">Rows per page; below 1 uses the default page size.</param>
    /// <param name="filter">Queued, sung or both; sung by default.</param>
    /// <param name="startDate">UTC; when set, only performances created at or after it.</param>
    Task<PaginatedResult<Performance>> ReadBySingerIdAsync(Guid singerId, int pageNumber = 0, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued, DateTime? startDate = null);

    /// <summary>Sung counts since <paramref name="since"/>; nothing sung is absent, not zero.</summary>
    /// <param name="singerIds">The singers to count; a singer with nothing sung is absent from the result.</param>
    /// <param name="since">UTC; compared against each performance's creation time.</param>
    Task<IReadOnlyDictionary<Guid, int>> CountSungSinceAsync(IEnumerable<Guid> singerIds, DateTime since);

    /// <summary>Distinct venues sung at, most recent first; pre-tracking performances skipped.</summary>
    /// <returns>At most <paramref name="count"/> visits; empty when <paramref name="count"/> is zero or less.</returns>
    Task<IReadOnlyList<RecentVenueVisit>> ReadRecentVenueVisitsBySingerAsync(Guid singerId, int count);

    /// <summary>Most recent venue per singer; one with none recorded is absent, not null.</summary>
    Task<IReadOnlyDictionary<Guid, RecentVenueVisit>> ReadLastVenueBySingersAsync(IEnumerable<Guid> singerIds);

    /// <summary>One page of the performances of one song, newest first.</summary>
    Task<PaginatedResult<Performance>> ReadByMediaIdAsync(Guid mediaId, int pageNumber = 0, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued);

    /// <summary>Deletes every queued performance; sung history stays.</summary>
    Task DeleteAllQueuedAsync();
}
