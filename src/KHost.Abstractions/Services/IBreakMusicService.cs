using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;

namespace KHost.Abstractions.Services;

/// <summary>
/// Suspended is not Paused: a host who paused break music meant it, and the automatic handoff
/// must not undo that when the song ends.
/// </summary>
public enum BreakMusicState { Stopped, Playing, Paused, Suspended }

/// <summary>
/// Owns when break music plays; the provider owns what. Everything a host does lands here, and
/// the two automatic transitions — yielding to a song and coming back after it — are the reason
/// this is a service rather than a button wired straight to a provider.
/// </summary>
public interface IBreakMusicService
{
    event EventHandler? StateChanged;

    IReadOnlyList<IBreakMusicProvider> Providers { get; }
    IBreakMusicProvider? ActiveProvider { get; }

    BreakMusicState State { get; }
    BreakMusicTrack? CurrentTrack { get; }

    /// <summary>Restores the venue's chosen provider. Call once at startup.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SetActiveProviderAsync(string sourceName, CancellationToken cancellationToken = default);

    Task<bool> StartAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SkipAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Yields to something that has audio of its own. Does nothing unless it is playing, so a
    /// paused or stopped bed is left where the host put it.
    /// </summary>
    Task SuspendAsync(CancellationToken cancellationToken = default);

    /// <summary>Brings back only what <see cref="SuspendAsync"/> took away.</summary>
    Task RestoreAsync(CancellationToken cancellationToken = default);
}
