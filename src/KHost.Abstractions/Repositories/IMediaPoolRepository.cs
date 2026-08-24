using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

public interface IMediaPoolRepository : IRepository<MediaPool>
{
    /// <summary>The pool with its entries loaded. The inherited read leaves them empty.</summary>
    Task<MediaPool?> ReadWithEntriesAsync(Guid id);

    /// <summary>
    /// Every playlist for a purpose, entries loaded, for a venue and the ones scoped to none. Selection
    /// resolves nested pools out of this rather than reading each one as it descends.
    /// </summary>
    Task<IReadOnlyList<MediaPool>> ReadAllWithEntriesAsync(PoolPurpose purpose, Guid? venueId);

    /// <summary>Replaces a pool's entries wholesale, which is how the editor saves a reordering.</summary>
    Task ReplaceEntriesAsync(Guid poolId, IReadOnlyList<MediaPoolEntry> entries);
}
