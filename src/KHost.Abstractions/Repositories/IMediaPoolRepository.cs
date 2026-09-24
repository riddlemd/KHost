using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

/// <summary>Playlists for break music and ads, and their ordered entries.</summary>
/// <remarks>
/// <para>Host-implemented. A plugin goes through <see cref="Services.IMediaPoolService"/>, which
/// announces <see cref="Messaging.Messages.MediaPoolsChanged"/>, refuses an entry list that would
/// let a pool reach itself through a child pool, and resets the pool's play cursor after an edit.
/// <see cref="ReplaceEntriesAsync"/> here checks none of that.</para>
/// <para>Search matches the pool name, case- and accent-insensitive, and honours
/// <see cref="MediaPoolSearchOptions"/>. Sort keys: <c>name</c> (default), <c>purpose</c>.</para>
/// </remarks>
public interface IMediaPoolRepository : IRepository<MediaPool>
{
    /// <summary>The pool with its entries loaded. The inherited read leaves them empty.</summary>
    /// <returns>Entries in play order, or null when no pool has this id.</returns>
    Task<MediaPool?> ReadWithEntriesAsync(Guid id);

    /// <summary>Every playlist for a purpose, entries loaded, for a venue plus venue-less ones.</summary>
    /// <param name="purpose">Only pools of this purpose are returned.</param>
    /// <param name="venueId">Null returns only the pools scoped to no venue.</param>
    Task<IReadOnlyList<MediaPool>> ReadAllWithEntriesAsync(PoolPurpose purpose, Guid? venueId);

    /// <summary>Replaces a pool's entries wholesale, which is how the editor saves a reordering.</summary>
    /// <param name="poolId">The pool whose entries are replaced.</param>
    /// <param name="entries">The new list in play order. Each entry's position is taken from its
    /// index here, not from the model, and an entry with an empty id is given a new one.</param>
    Task ReplaceEntriesAsync(Guid poolId, IReadOnlyList<MediaPoolEntry> entries);
}
