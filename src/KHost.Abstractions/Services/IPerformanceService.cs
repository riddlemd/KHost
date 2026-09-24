using KHost.Abstractions.Models;
using System.Text.Json.Serialization;

namespace KHost.Abstractions.Services;

/// <summary>Every singer's turns: the songs each has waiting, and the history of what was sung.</summary>
/// <remarks>A performance is queued while its <see cref="Performance.QueuePosition"/> is set, and
/// joins the history once it is dequeued; positions count from 1 within each singer's own list.
/// Where singers stand relative to each other is <see cref="ISingerQueueService"/>'s.
///
/// <para>A plugin TAKES it to enqueue: compose <see cref="ISingerQueueService.SelectedUserId"/> with
/// <see cref="CreateAndEnqueueAsync"/>. A host singleton, callable from any thread. Every create,
/// update, delete, move and dequeue announces
/// <see cref="KHost.Abstractions.Messaging.Messages.PerformancesChanged"/>. Deleting a performance
/// also cancels its media's download if one is still in flight.</para></remarks>
public interface IPerformanceService : IRepositoryService<Performance>
{
    /// <summary>Every performance matching <paramref name="filter"/>; queued ones by default.</summary>
    Task<PaginatedResult<Performance>> ReadAllAsync(int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.Queued);

    /// <summary>One singer's performances, newest first; their history by default.</summary>
    /// <param name="singerId">The singer whose performances are read.</param>
    /// <param name="pageNumber">1-based page to read; below 1 reads page 1.</param>
    /// <param name="pageSize">Rows per page; below 1 uses the default page size.</param>
    /// <param name="filter">Queued, sung or both; sung by default.</param>
    /// <param name="startDate">When given, only performances created at or after it, in UTC.</param>
    Task<PaginatedResult<Performance>> ReadBySingerIdAsync(Guid singerId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued, DateTime? startDate = null);

    /// <summary>Sung counts since <paramref name="since"/> by singer; none is absent, not zero.</summary>
    /// <remarks><paramref name="since"/> is compared against creation times in UTC.</remarks>
    Task<IReadOnlyDictionary<Guid, int>> CountSungSinceAsync(IEnumerable<Guid> singerIds, DateTime since);

    /// <summary>Distinct venues sung at, most recent first; performances with no venue are skipped.</summary>
    /// <returns>At most <paramref name="count"/>; empty when it is zero or less.</returns>
    Task<IReadOnlyList<RecentVenueVisit>> ReadRecentVenueVisitsBySingerAsync(Guid singerId, int count);

    /// <summary>The venue each singer last sang at, keyed by singer; a singer who has sung at none is
    /// absent.</summary>
    Task<IReadOnlyDictionary<Guid, RecentVenueVisit>> ReadLastVenueBySingersAsync(IEnumerable<Guid> singerIds);

    /// <summary>The performances of one song; its history by default.</summary>
    Task<PaginatedResult<Performance>> ReadByMediaIdAsync(Guid mediaId, int pageNumber = 1, int pageSize = 0, PerformanceFilter filter = PerformanceFilter.UnQueued);

    /// <summary>The first song a singer has waiting, or null when they have none.</summary>
    Task<Performance?> ReadSingersNextPerformanceAsync(Guid singerId);

    /// <summary>Every queued performance, across all singers, ordered by each one's position in its
    /// singer's list.</summary>
    Task<List<Performance>> ReadQueuedAsync();

    /// <summary>Puts a song at the end of a singer's list, applying the rules that guard it.</summary>
    /// <returns>The saved performance, or null when it was refused: the venue's duplicate-song
    /// warning was shown and declined, or the gate that owns the media refused it for
    /// <see cref="MediaAction.Queue"/>, in which case the reason is flashed to the host.</returns>
    /// <remarks>Fills what the caller left unset: the name sung under (the singer's own), the venue
    /// (the selected one) and the creation time. May show the host a confirmation first, so it can
    /// wait on a person.</remarks>
    Task<Performance?> CreateAndEnqueueAsync(Performance performance);

    /// <summary>Takes a performance off the queue into the history, as a finished song does.</summary>
    /// <remarks>Leaves the performance alone when it does not belong to <paramref name="singerId"/>. To
    /// drop a song that was never sung, delete it instead.</remarks>
    Task DequeueAsync(Guid singerId, Guid performanceId);

    /// <summary>Moves a song one place earlier in its singer's list. Does nothing at the top.</summary>
    Task MoveUpInQueueAsync(Guid singerId, Guid performanceId);

    /// <summary>Moves a song one place later in its singer's list. Does nothing at the bottom.</summary>
    Task MoveDownInQueueAsync(Guid singerId, Guid performanceId);

    /// <summary>Moves a song to the end of its singer's list.</summary>
    Task MoveToEndOfQueueAsync(Guid singerId, Guid performanceId);

    /// <summary>Moves a song to <paramref name="newIndex"/>, 0-based, in its singer's list.</summary>
    /// <remarks>An index past either end is clamped. A performance not in that singer's list is left
    /// alone.</remarks>
    Task MoveToIndexAsync(Guid singerId, Guid performanceId, int newIndex);

    /// <summary>Deletes every queued performance, for every singer. History is kept.</summary>
    Task DeleteAllQueuedAsync();
}
