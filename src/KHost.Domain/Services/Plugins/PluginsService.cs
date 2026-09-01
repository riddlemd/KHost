using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace KHost.Domain.Services.Plugins;

public class PluginsService : BaseService, IPluginsService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IMessageBroker _broker;
    private readonly ICacheService _cache;
    private readonly IPluginRegistry _registry;

    public PluginsService(ILogger<PluginsService> logger, ICacheService cache, IPluginRegistry registry, IMessageBroker broker)
        : base(logger)
    {
        _broker = broker;
        _cache = cache;
        _registry = registry;
    }

    public IReadOnlyList<DiscoveredPlugin> Plugins => _registry.Plugins;

    public bool RestartRequired { get; private set; }

    public async Task<IReadOnlySet<string>> ReadEnabledIdsAsync()
    {
        var state = await LoadStateAsync();

        return state.EnabledPluginIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetEnabledAsync(string pluginId, bool enabled)
    {
        await MutateStateAsync(state =>
        {
            state.EnabledPluginIds.RemoveAll(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase));

            if (enabled)
                state.EnabledPluginIds.Add(pluginId);
        });

        Logger.LogInformation("Plugin '{PluginId}' {State}; restart required", pluginId, enabled ? "enabled" : "disabled");
    }

    public async Task<Dictionary<string, JsonElement>> ReadSettingsAsync(string pluginId)
    {
        var state = await LoadStateAsync();

        return state.Settings.GetValueOrDefault(pluginId) ?? [];
    }

    public async Task SaveSettingsAsync(string pluginId, Dictionary<string, JsonElement> values)
    {
        await MutateStateAsync(state => state.Settings[pluginId] = values);

        Logger.LogInformation("Settings saved for plugin '{PluginId}'; restart required", pluginId);
    }

    private async Task<PluginsState> LoadStateAsync()
        => await _cache.LoadAsync<PluginsState>(PluginsState.CacheKey) ?? new PluginsState();

    private async Task MutateStateAsync(Action<PluginsState> mutate)
    {
        await _lock.WaitAsync();

        try
        {
            var state = await LoadStateAsync();

            mutate(state);

            await _cache.SaveAsync(PluginsState.CacheKey, state);

            RestartRequired = true;
        }
        finally
        {
            _lock.Release();
        }

        _broker.Announce(new PluginsChanged());
    }
}
