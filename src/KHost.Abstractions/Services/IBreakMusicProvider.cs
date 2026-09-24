using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Music between singers; asked to play, not searched, with no files or stream.</summary>
/// <remarks>An extension point: a plugin IMPLEMENTS it and the host discovers it, listing it as
/// "Break music" on the Plugins page. The host ships one itself, fed from the library's playlists.
/// A venue picks one provider by <see cref="SourceName"/>; <see cref="IBreakMusicService"/> alone
/// decides when it plays, and calls these members one at a time, never concurrently.
///
/// <para>The plugin's object is one singleton shared across every extension interface it
/// implements, and is called from any thread. A provider driving another app may be changed behind
/// the host's back; <see cref="ReadPlaybackAsync"/> is how the host catches up.</para></remarks>
public interface IBreakMusicProvider
{
    /// <summary>What the console calls this provider.</summary>
    string DisplayName { get; }

    /// <summary>Stable key the host stores to remember which provider a venue chose.</summary>
    /// <remarks>Matched case-insensitively. Renaming it orphans every venue that chose it, which
    /// then falls back to the library provider.</remarks>
    string SourceName { get; }

    /// <summary>True when the host carries the sound (needs a screen); false for another app.</summary>
    /// <remarks>Decides who sets the level. True: the display provider applies the venue's volume
    /// to the channel, and <see cref="SetVolumeAsync"/> is never called. False: the host pushes
    /// the venue's volume through <see cref="SetVolumeAsync"/> on start and on a venue change.</remarks>
    bool RendersThroughHost { get; }

    /// <summary>What is playing now, or null when nothing is. Named on the console and, when the
    /// venue turns it on, in a corner of the screen.</summary>
    /// <remarks>Publish <see cref="KHost.Abstractions.Messaging.Messages.BreakMusicTrackChanged"/>
    /// carrying this provider's own <see cref="SourceName"/> whenever it moves, or the console will
    /// not notice. A message naming any other source, or sent while this provider is not the
    /// venue's active one, is ignored. Hearing it, the host also re-reads
    /// <see cref="ReadPlaybackAsync"/>.</remarks>
    BreakMusicTrack? CurrentTrack { get; }

    /// <summary>What this is doing now, or null if it can't tell; another app may already play.</summary>
    /// <remarks>Null means "cannot tell", not the same as <see cref="BreakMusicPlayback.Stopped"/>:
    /// the host keeps its own idea of the state rather than adopting one. Asked at startup, before
    /// the host suspends for a song, and on each <c>BreakMusicTrackChanged</c>. A throw is logged
    /// and treated as null.
    ///
    /// <para>Has a default body returning null. Leave it for a provider the host fully controls;
    /// override it for one driving another app, or the console shows "stopped" over music the room
    /// can hear, and a host's pause in that app is overridden.</para></remarks>
    Task<BreakMusicPlayback?> ReadPlaybackAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<BreakMusicPlayback?>(null);

    /// <summary>Begins playing from wherever this provider's own list picks up.</summary>
    /// <returns>False when there was nothing to play, or nowhere to play it; the host then stays
    /// stopped.</returns>
    /// <remarks>Also how the host brings music back after a song: it stops the provider outright
    /// to suspend, then calls this rather than <see cref="ResumeAsync"/>.</remarks>
    Task<bool> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Holds the current track where it is. Asked only while playing.</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Continues a paused track. Asked only while paused.</summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>Ends the session. <paramref name="fadeDuration"/> is a hint a provider may ignore.</summary>
    /// <remarks>Called with a short fade when a song or an ad with its own audio takes the room, and
    /// with none when the host stops the music or switches provider. The call is awaited, so a
    /// provider that blocks for its fade holds the song's start for that long.</remarks>
    Task StopAsync(TimeSpan? fadeDuration = null, CancellationToken cancellationToken = default);

    /// <summary>Moves on to the next track, which should start playing even from a pause.</summary>
    /// <remarks>The host treats a skip from pause as playing afterwards.</remarks>
    Task SkipAsync(CancellationToken cancellationToken = default);

    /// <summary>0 to 1. An external provider may only be able to approximate it.</summary>
    /// <remarks>Only called when <see cref="RendersThroughHost"/> is false.</remarks>
    Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default);
}
