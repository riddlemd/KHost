using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>The host's playlists for break music and ads, and the pick of what plays next from one.
/// </summary>
/// <remarks>Host-owned; a plugin may take it to read or pick from a playlist and has nothing to
/// implement. A host singleton, callable from any thread. Every create, update, delete and entry
/// replacement announces <see cref="KHost.Abstractions.Messaging.Messages.MediaPoolsChanged"/>.
/// </remarks>
public interface IMediaPoolService : IRepositoryService<MediaPool>
{
    /// <summary>The pool with its entries loaded. The inherited read leaves them empty.</summary>
    /// <returns>Null when no pool has that id.</returns>
    Task<MediaPool?> ReadWithEntriesAsync(Guid id);

    /// <summary>Every pool of <paramref name="purpose"/> open to <paramref name="venueId"/>, with its
    /// entries.</summary>
    /// <remarks>A pool belonging to every venue is always included; a null
    /// <paramref name="venueId"/> gets only those.</remarks>
    Task<IReadOnlyList<MediaPool>> ReadAllWithEntriesAsync(PoolPurpose purpose, Guid? venueId);

    /// <summary>Replaces a pool's entries; refused if the result lets the pool reach itself.</summary>
    /// <returns>False when the pool does not exist or the entries would close a loop through nested
    /// pools; nothing is saved then.</returns>
    /// <remarks>A saved change starts the pool's selection over, as <see cref="ResetSelection"/>
    /// does.</remarks>
    Task<bool> ReplaceEntriesAsync(Guid poolId, IReadOnlyList<MediaPoolEntry> entries);

    /// <summary>Next entry, or null if nothing is playable; advances the cursor and history.</summary>
    /// <remarks>Follows the pool's selection mode and no-repeat rule, descending into nested pools
    /// open to <paramref name="venueId"/>. The returned entry names a media row, never a nested
    /// pool. The cursor and history are not kept across a restart.</remarks>
    Task<MediaPoolEntry?> SelectNextAsync(Guid poolId, Guid? venueId);

    /// <summary>Forgets a pool's cursor and history: what a host expects "start over" to do.</summary>
    void ResetSelection(Guid poolId);
}
