using KHost.Abstractions.Models;

namespace KHost.Abstractions.Repositories;

/// <summary>Visualisation playlists and their ordered entries.</summary>
/// <remarks>
/// <para>Host-implemented. A plugin goes through <see cref="Services.IVisualisationPlaylistService"/>,
/// which announces <see cref="Messaging.Messages.VisualisationPlaylistsChanged"/>, holds each entry's
/// settings to their ranges and restarts the playlist's rotation after an edit.
/// <see cref="ReplaceEntriesAsync"/> here does none of that.</para>
/// <para>Search matches the playlist name, case- and accent-insensitive. Sort keys: <c>name</c>
/// (default).</para>
/// </remarks>
public interface IVisualisationPlaylistRepository : IRepository<VisualisationPlaylist>
{
    /// <summary>The playlist with its entries loaded, in play order; null when there is none.</summary>
    Task<VisualisationPlaylist?> ReadWithEntriesAsync(Guid id);

    /// <summary>Every playlist with its entries loaded, in play order.</summary>
    Task<IReadOnlyList<VisualisationPlaylist>> ReadAllWithEntriesAsync();

    /// <summary>Replaces a playlist's entries wholesale.</summary>
    /// <param name="playlistId">The playlist whose entries are replaced.</param>
    /// <param name="entries">The new list in play order. Each entry's position is taken from its
    /// index here, not from the model, and an entry with an empty id is given a new one.</param>
    Task ReplaceEntriesAsync(Guid playlistId, IReadOnlyList<VisualisationEntry> entries);
}
