using KHost.Plugins.Sdk.Models;

namespace KHost.Plugins.Sdk.Services;

/// <summary>
/// Music between singers. Not a media provider: this one is asked to play, not searched — a
/// provider driving Spotify or Pandora on the same machine hands the host no files and no stream,
/// only the transport buttons.
/// </summary>
public interface IBreakMusicProvider
{
    string DisplayName { get; }

    /// <summary>Stable key the host stores to remember which provider a venue chose.</summary>
    string SourceName { get; }

    /// <summary>
    /// True when the host carries the sound — the library provider, whose audio rides the screen's
    /// second channel and therefore reaches a Cast device and needs a screen connected. False for
    /// one driving another app, whose sound comes out of that app's own output where the host
    /// cannot route it, mix it, or send it anywhere.
    /// </summary>
    bool RendersThroughHost { get; }

    /// <summary>
    /// Publish a <c>BreakMusicTrackChanged</c> carrying this provider's <see cref="SourceName"/>
    /// whenever this moves on its own, or the console will not know the track turned over.
    /// </summary>
    BreakMusicTrack? CurrentTrack { get; }

    /// <summary>False when there was nothing to play, or nowhere to play it.</summary>
    Task<bool> StartAsync(CancellationToken cancellationToken = default);

    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>Ends the session. <paramref name="fadeDuration"/> is a hint a provider may ignore.</summary>
    Task StopAsync(TimeSpan? fadeDuration = null, CancellationToken cancellationToken = default);

    Task SkipAsync(CancellationToken cancellationToken = default);

    /// <summary>0 to 1. An external provider may only be able to approximate it.</summary>
    Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default);
}
