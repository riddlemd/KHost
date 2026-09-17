using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaPoolService : IRepositoryService<MediaPool>
{
    /// <summary>The pool with its entries loaded. The inherited read leaves them empty.</summary>
    Task<MediaPool?> ReadWithEntriesAsync(Guid id);

    Task<IReadOnlyList<MediaPool>> ReadAllWithEntriesAsync(PoolPurpose purpose, Guid? venueId);

    /// <summary>Replaces a pool's entries; refused if the result lets the pool reach itself.</summary>
    Task<bool> ReplaceEntriesAsync(Guid poolId, IReadOnlyList<MediaPoolEntry> entries);

    /// <summary>Next entry, or null if nothing is playable; advances the cursor and history.</summary>
    Task<MediaPoolEntry?> SelectNextAsync(Guid poolId, Guid? venueId);

    /// <summary>Forgets a pool's cursor and history: what a host expects "start over" to do.</summary>
    void ResetSelection(Guid poolId);
}
