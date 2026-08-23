using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Plugins;

/// <summary>
/// The second half of loading. Discovery runs before the container exists and so runs no plugin
/// code; this runs afterwards and gives each entry point its one chance to do something.
/// </summary>
internal sealed class PluginInitializer : IPluginInitializer
{
    private readonly ILogger<PluginInitializer> _logger;
    private readonly IEnumerable<LoadedPlugin> _plugins;

    public PluginInitializer(ILogger<PluginInitializer> logger, IEnumerable<LoadedPlugin> plugins)
    {
        _logger = logger;
        _plugins = plugins;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var plugin in _plugins)
        {
            try
            {
                await plugin.EntryPoint.InitializeAsync(plugin.Context, cancellationToken);
            }
            catch (Exception ex)
            {
                // Marked and logged, never rethrown: one plugin's bad setup must not be the reason
                // a venue cannot open the console. Its providers stay registered and may still work.
                _logger.LogError(ex, "Plugin '{Plugin}' failed to initialize", plugin.Discovered.DisplayName);

                plugin.Discovered.Status = PluginStatus.Errored;
                plugin.Discovered.Error = $"Initialization failed: {ex.Message}";
            }
        }
    }
}
