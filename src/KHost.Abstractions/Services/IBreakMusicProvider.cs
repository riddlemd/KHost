using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Music between singers; asked to play, not searched, with no files or stream.</summary>
public interface IBreakMusicProvider
{
    string DisplayName { get; }

    /// <summary>Stable key the host stores to remember which provider a venue chose.</summary>
    string SourceName { get; }

    /// <summary>True when the host carries the sound (needs a screen); false for another app.</summary>
    bool RendersThroughHost { get; }

    /// <summary>Publish <c>BreakMusicTrackChanged</c> on its own move, or the console won't notice.</summary>
    BreakMusicTrack? CurrentTrack { get; }

    /// <summary>What this is doing now, or null if it can't tell; another app may already play.</summary>
    /// <remarks>Null means "cannot tell", not the same as <see cref="BreakMusicPlayback.Stopped"/>.</remarks>
    Task<BreakMusicPlayback?> ReadPlaybackAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<BreakMusicPlayback?>(null);

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
