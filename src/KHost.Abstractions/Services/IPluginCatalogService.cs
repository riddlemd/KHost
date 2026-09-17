using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>Reads the installable-plugins list, fetched only when the browse list opens.</summary>
public interface IPluginCatalogService
{
    /// <summary>The catalog last read this process, from cache or network. Null until one is read.</summary>
    PluginCatalogSnapshot? Current { get; }

    /// <summary>Why the last fetch failed, shown beside a stale catalog. Null after a good one.</summary>
    string? LastError { get; }

    /// <summary>Cached catalog, refetched only when aged; a dead network returns the stale copy.</summary>
    Task<PluginCatalogSnapshot?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches regardless of age. Keeps the cached copy on failure.</summary>
    Task<PluginCatalogSnapshot?> RefreshAsync(CancellationToken cancellationToken = default);
}
