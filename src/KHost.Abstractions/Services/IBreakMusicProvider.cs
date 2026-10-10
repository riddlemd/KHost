using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Music between singers; asked to play, not searched, with no files or stream.</summary>
/// <remarks>An extension point: a plugin IMPLEMENTS it and the host discovers it, listing it as
/// "Break music" on the Plugins page. The host ships one itself, fed from the library's playlists.
/// The host plays one provider at a time, chosen by <see cref="SourceName"/> in one app-wide
/// setting; <see cref="IBreakMusicService"/> alone decides when it plays, and calls these members one at a time, never concurrently.
///
/// <para>The plugin's object is one singleton shared across every extension interface it
/// implements, and is called from any thread. A provider driving another app may be changed behind
/// the host's back; <see cref="ReadPlaybackAsync"/> is how the host catches up.</para></remarks>
[PluginExtensionPoint("Break music", Order = 3)]
public interface IBreakMusicProvider
{
    /// <summary>What the console calls this provider.</summary>
    string DisplayName { get; }

    /// <summary>Stable key the host stores to remember which provider was chosen.</summary>
    /// <remarks>Matched case-insensitively. Renaming it orphans a saved choice, which then falls
    /// back to the library provider.</remarks>
    string SourceName { get; }

    /// <summary>What is playing now, or null when nothing is. Named on the console and, when the
    /// venue turns it on, in a corner of the screen.</summary>
    /// <remarks>Publish <see cref="KHost.Abstractions.Messaging.Messages.BreakMusicTrackChanged"/>
    /// carrying this provider's own <see cref="SourceName"/> whenever it moves, or the console will
    /// not notice. A message naming any other source, or sent while this provider is not the
    /// active one, is ignored. Hearing it, the host also re-reads
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
    /// to suspend, then calls this rather than <see cref="ResumeAsync"/>.
    ///
    /// <para>Throw <see cref="KHostException"/> instead of returning false when there is a reason
    /// a host should see — the console flashes <see cref="KHostException.WhatHappened"/>. A plain
    /// false reads as the host's own library provider: no playlist chosen, or no screen connected.
    /// Use it only when the refusal truly looks like that one; anything else needs the exception,
    /// or a host spends the night chasing advice that does not apply to this provider.</para></remarks>
    Task<bool> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Holds the current track where it is. Asked only while playing.</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Continues a paused track. Asked only while paused.</summary>
    /// <remarks>May throw <see cref="KHostException"/> for the same reason <see cref="StartAsync"/>
    /// can: the console reads <see cref="KHostException.WhatHappened"/> back to the host.</remarks>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>Ends the session. <paramref name="fadeDuration"/> is a hint a provider may ignore.</summary>
    /// <remarks>Called with <see cref="IBreakMusicSettings.FadeDuration"/> when a song or an ad with
    /// its own audio takes the room, and with none when the host stops the music or switches provider. The call is awaited, so a
    /// provider that blocks for its fade holds the song's start for that long.</remarks>
    Task StopAsync(TimeSpan? fadeDuration = null, CancellationToken cancellationToken = default);

    /// <summary>Moves on to the next track, which should start playing even from a pause.</summary>
    /// <remarks>The host treats a skip from pause as playing afterwards.</remarks>
    Task SkipAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the provider's level, 0 to 1.</summary>
    /// <remarks>The host does not call this; a provider plays at its own level. It stays on the
    /// contract so a provider built against it keeps loading.</remarks>
    Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default);
}
