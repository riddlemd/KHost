using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>Reads the installable-plugins list, fetched only when the browse list opens.</summary>
/// <remarks>Host-only: the Plugins page's Available tab. A plugin has no business with it. The
/// catalog is the trust root for installs, so a document in a schema this host cannot read is
/// refused whole rather than read in part. A host singleton, callable from any thread; concurrent
/// loads do not overlap. Announces
/// <see cref="KHost.Abstractions.Messaging.Messages.PluginCatalogChanged"/> when a different catalog
/// is taken up, from the saved copy or the network.</remarks>
public interface IPluginCatalogService
{
    /// <summary>The catalog last read this process, from cache or network. Null until one is read.</summary>
    PluginCatalogSnapshot? Current { get; }

    /// <summary>Why the last fetch failed, shown beside a stale catalog. Null after a good one.</summary>
    string? LastError { get; }

    /// <summary>Cached catalog, refetched only when aged; a dead network returns the stale copy.</summary>
    /// <returns>Null only when no catalog has ever been read and this fetch failed too; the reason
    /// is in <see cref="LastError"/>.</returns>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/> fires;
    /// every other failure is recorded in <see cref="LastError"/> instead.</exception>
    Task<PluginCatalogSnapshot?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches regardless of age. Keeps the cached copy on failure.</summary>
    /// <inheritdoc cref="GetAsync" path="/returns"/>
    /// <inheritdoc cref="GetAsync" path="/exception"/>
    Task<PluginCatalogSnapshot?> RefreshAsync(CancellationToken cancellationToken = default);
}
