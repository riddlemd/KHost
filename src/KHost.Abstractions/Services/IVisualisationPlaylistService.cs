using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>The host's visualisation playlists, and the pick of which entry draws under the next
/// song.</summary>
/// <remarks>Host-owned; a plugin display may take it to draw what a venue chose and has nothing to
/// implement. A host singleton, callable from any thread. Every create, update, delete and entry
/// replacement announces <see cref="KHost.Abstractions.Messaging.Messages.VisualisationPlaylistsChanged"/>.
/// </remarks>
public interface IVisualisationPlaylistService : IRepositoryService<VisualisationPlaylist>
{
    /// <summary>The playlist with its entries loaded, in play order; null when there is none.</summary>
    Task<VisualisationPlaylist?> ReadWithEntriesAsync(Guid id);

    /// <summary>Every playlist with its entries, in play order.</summary>
    Task<IReadOnlyList<VisualisationPlaylist>> ReadAllWithEntriesAsync();

    /// <summary>Replaces a playlist's entries, each held to its settings' ranges.</summary>
    /// <returns>False when there is no such playlist; nothing is saved then.</returns>
    /// <remarks>A saved change starts the playlist's rotation over.</remarks>
    Task<bool> ReplaceEntriesAsync(Guid playlistId, IReadOnlyList<VisualisationEntry> entries);

    /// <summary>The entry for the next song, advancing the playlist's rotation; null when there is
    /// no such playlist or it is empty.</summary>
    /// <remarks>In order it takes the next entry, wrapping at the end; shuffled it takes any entry
    /// but the one just picked, when there is another. Call it once per song. The rotation is not
    /// kept across a restart.</remarks>
    Task<VisualisationEntry?> SelectNextAsync(Guid playlistId);
}
