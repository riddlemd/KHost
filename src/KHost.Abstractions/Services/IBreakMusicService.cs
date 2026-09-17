using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.Abstractions.Services;

/// <summary>Suspended is not Paused; an automatic handoff must not undo a host's pause.</summary>
public enum BreakMusicState { Stopped, Playing, Paused, Suspended }

/// <summary>Owns when break music plays; the provider owns what, hence a service, not a button.</summary>
public interface IBreakMusicService
{

    IReadOnlyList<IBreakMusicProvider> Providers { get; }
    IBreakMusicProvider? ActiveProvider { get; }

    /// <summary>The provider fed by the host's own playlists, unlike RendersThroughHost.</summary>
    IBreakMusicProvider? LibraryProvider { get; }

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

    /// <summary>Yields to something with audio of its own; does nothing unless it is playing.</summary>
    Task SuspendAsync(CancellationToken cancellationToken = default);

    /// <summary>Brings back only what <see cref="SuspendAsync"/> took away.</summary>
    Task RestoreAsync(CancellationToken cancellationToken = default);
}
