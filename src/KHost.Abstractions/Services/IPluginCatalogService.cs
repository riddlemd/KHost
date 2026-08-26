using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>
/// Reads the published list of installable plugins. Never fetched at startup — a console runs on
/// whatever wifi the room has, so the network is touched only when a host opens the browse list.
/// </summary>
public interface IPluginCatalogService
{
    /// <summary>The catalog last read this process, from cache or network. Null until one is read.</summary>
    PluginCatalogSnapshot? Current { get; }

    /// <summary>Why the last fetch failed, for the page to show beside a stale catalog. Null after a good one.</summary>
    string? LastError { get; }

    /// <summary>
    /// The cached catalog, fetching only when there is none or it has aged past its lifetime.
    /// Returns the stale copy when the fetch fails, so a dead network shows an old list, not none.
    /// </summary>
    Task<PluginCatalogSnapshot?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches regardless of age. Keeps the cached copy on failure.</summary>
    Task<PluginCatalogSnapshot?> RefreshAsync(CancellationToken cancellationToken = default);
}
