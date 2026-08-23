using System.Collections.Concurrent;
using KHost.Abstractions.Services;

namespace KHost.Domain.Services.Plugins;

// Split out of PluginLibrary so it has no dependencies: PluginLibrary depends on IPerformanceService,
// and PerformanceService depends on IPluginImportCancellation, so keeping this on PluginLibrary itself
// made the two constructors cycle.
public class PluginImportCancellations : IPluginImportCancellation
{
    // Keyed by media id, one entry per row currently Downloading. Entries are added only when
    // PluginLibrary.BeginImportAsync creates a NEW row, and removed only when the plugin settles
    // that row via Complete/Fail/Discard — so a Cancel after settling always finds nothing and no-ops.
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _imports = new();

    public CancellationToken Register(Guid mediaId)
    {
        var cts = new CancellationTokenSource();
        _imports[mediaId] = cts;
        return cts.Token;
    }

    public CancellationToken TokenForInFlight(Guid mediaId) =>
        _imports.GetOrAdd(mediaId, _ => new CancellationTokenSource()).Token;

    public void Remove(Guid mediaId)
    {
        if (_imports.TryRemove(mediaId, out var cts))
            cts.Dispose();
    }

    public Task CancelImportAsync(Guid mediaId)
    {
        if (_imports.TryGetValue(mediaId, out var cts))
            cts.Cancel();

        return Task.CompletedTask;
    }

    public void CancelAllImports()
    {
        foreach (var cts in _imports.Values)
            cts.Cancel();
    }
}
