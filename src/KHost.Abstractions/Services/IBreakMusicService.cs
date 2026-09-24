using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.Abstractions.Services;

/// <summary>Suspended is not Paused; an automatic handoff must not undo a host's pause.</summary>
public enum BreakMusicState
{
    /// <summary>Nothing is playing, and nothing will come back on its own.</summary>
    Stopped,

    /// <summary>Break music is reaching the room.</summary>
    Playing,

    /// <summary>The host paused it; it stays paused until the host resumes it.</summary>
    Paused,

    /// <summary>Taken off while a song or an ad has the room; returns by itself when the room is
    /// free. Reached only from <see cref="Playing"/>.</summary>
    Suspended
}

/// <summary>Owns when break music plays; the provider owns what, hence a service, not a button.</summary>
/// <remarks>Host-owned. A plugin supplies music by implementing <see cref="IBreakMusicProvider"/>,
/// not this; it may take this in its constructor to read the state or drive the transport. A host
/// singleton, callable from any thread: state changes never interleave, and each announces
/// <see cref="KHost.Abstractions.Messaging.Messages.BreakMusicChanged"/>.
/// Follows the selected venue: a venue change switches to its chosen provider and re-applies its
/// volume.</remarks>
public interface IBreakMusicService
{

    /// <summary>Every provider the host knows, its own library provider and plugins' alike.</summary>
    IReadOnlyList<IBreakMusicProvider> Providers { get; }

    /// <summary>The provider the venue chose, or the library provider when that one is not loaded.
    /// Null only before <see cref="InitializeAsync"/> or when no provider exists.</summary>
    IBreakMusicProvider? ActiveProvider { get; }

    /// <summary>The provider fed by the host's own playlists, unlike RendersThroughHost.</summary>
    /// <remarks>Also the fallback when a venue names a provider that is not loaded.</remarks>
    IBreakMusicProvider? LibraryProvider { get; }

    /// <summary>Where the bed stands, as the host sees it.</summary>
    /// <remarks>Adopted from <see cref="IBreakMusicProvider.ReadPlaybackAsync"/> when a provider can
    /// tell, since another app may be moved behind the host's back.</remarks>
    BreakMusicState State { get; }

    /// <summary>The active provider's current track, or null.</summary>
    BreakMusicTrack? CurrentTrack { get; }

    /// <summary>Restores the venue's chosen provider. Call once at startup.</summary>
    /// <remarks>Host-called; a plugin should not call it.</remarks>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Switches to the provider with this <see cref="IBreakMusicProvider.SourceName"/>,
    /// stopping the outgoing one first.</summary>
    /// <remarks>An unknown name, or the provider already active, does nothing.</remarks>
    Task SetActiveProviderAsync(string sourceName, CancellationToken cancellationToken = default);

    /// <summary>Starts the active provider and applies the venue's volume.</summary>
    /// <returns>False when there is no provider, the provider had nothing to play, or a song or an
    /// ad with its own audio holds the room.</returns>
    Task<bool> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Pauses the bed. Does nothing unless it is playing.</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Resumes a paused bed. Does nothing unless paused, or while a song holds the room.</summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the bed outright, from any state.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Moves to the next track and leaves it playing, even from a pause.</summary>
    /// <remarks>Does nothing when stopped, or while a song holds the room.</remarks>
    Task SkipAsync(CancellationToken cancellationToken = default);

    /// <summary>Yields to something with audio of its own; does nothing unless it is playing.</summary>
    /// <remarks>Marks the room as taken either way, so <see cref="StartAsync"/>,
    /// <see cref="ResumeAsync"/> and <see cref="SkipAsync"/> refuse until <see cref="RestoreAsync"/>.
    /// Playback calls this on every song load and for an ad with its own audio.</remarks>
    Task SuspendAsync(CancellationToken cancellationToken = default);

    /// <summary>Brings back only what <see cref="SuspendAsync"/> took away.</summary>
    /// <remarks>Frees the room, then restarts the provider only if the state is
    /// <see cref="BreakMusicState.Suspended"/>; a bed the host paused or stopped stays put. Falls to
    /// Stopped if the provider will not start.</remarks>
    Task RestoreAsync(CancellationToken cancellationToken = default);
}
