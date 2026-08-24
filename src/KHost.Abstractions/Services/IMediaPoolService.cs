using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaPoolService : IRepositoryService<MediaPool>
{
    /// <summary>The pool with its entries loaded. The inherited read leaves them empty.</summary>
    Task<MediaPool?> ReadWithEntriesAsync(Guid id);

    Task<IReadOnlyList<MediaPool>> ReadAllWithEntriesAsync(PoolPurpose purpose, Guid? venueId);

    /// <summary>
    /// Replaces a pool's entries. Refused when the result would let the pool reach itself — the
    /// selector caps its own depth, but a cycle on disk is a fault a host cannot see or undo.
    /// </summary>
    Task<bool> ReplaceEntriesAsync(Guid poolId, IReadOnlyList<MediaPoolEntry> entries);

    /// <summary>
    /// The next entry out of the pool, or null when the tree holds nothing playable. Advances the
    /// pool's own sequential cursor and no-repeat history.
    /// </summary>
    Task<MediaPoolEntry?> SelectNextAsync(Guid poolId, Guid? venueId);

    /// <summary>Forgets a pool's cursor and history — what a host expects "start over" to do.</summary>
    void ResetSelection(Guid poolId);
}
