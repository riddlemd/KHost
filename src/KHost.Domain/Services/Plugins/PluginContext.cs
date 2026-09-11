using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Plugins.Secrets;
using System.Text.Json;

namespace KHost.Domain.Services.Plugins;

public class PluginContext : IPluginContext
{
    private readonly Dictionary<string, JsonElement> _values;
    private readonly Dictionary<string, JsonElement> _defaults;
    private readonly DiscoveredPlugin _plugin;
    private readonly IPluginSecretStore _secrets;
    private readonly string _pluginId;

    public PluginContext(
        PluginManifest manifest,
        Dictionary<string, JsonElement>? storedValues,
        DiscoveredPlugin plugin,
        IPluginSecretStore secrets)
    {
        _plugin = plugin;
        _secrets = secrets;

        // Taken from the manifest the host read, never from the plugin. It is what keeps one
        // plugin's secrets out of another's reach, so a caller must have no say in it.
        _pluginId = manifest.Id.ToString();

        // Case-insensitive: stored keys pass through camelCase serialization, manifests may not.
        _values = new(storedValues ?? [], StringComparer.OrdinalIgnoreCase);
        _defaults = new(StringComparer.OrdinalIgnoreCase);

        foreach (var setting in manifest.Settings.Where(s => s.Default is not null))
            _defaults[setting.Key] = setting.Default!.Value;

    }


    public T? GetSetting<T>(string key)
    {
        if (!_values.TryGetValue(key, out var element) && !_defaults.TryGetValue(key, out element))
            return default;

        try
        {
            return element.Deserialize<T>(JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public TSettings BindSettings<TSettings>() where TSettings : new()
    {
        var merged = new Dictionary<string, JsonElement>(_defaults, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in _values)
            merged[key] = value;

        try
        {
            return JsonSerializer.SerializeToElement(merged).Deserialize<TSettings>(JsonSerializerOptions.Web) ?? new();
        }
        catch (JsonException)
        {
            // One malformed stored value falls back to the type's own defaults.
            return new();
        }
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
        => _secrets.ReadAsync(_pluginId, key, cancellationToken);

    public Task SetSecretAsync(string key, string? value, CancellationToken cancellationToken = default)
        => _secrets.WriteAsync(_pluginId, key, value, cancellationToken);

    /// <summary>Reported from a plugin's own background work, so the list is not appended to bare.</summary>
    public void ReportWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        lock (_plugin.Warnings)
        {
            if (!_plugin.Warnings.Contains(message))
                _plugin.Warnings.Add(message);
        }
    }
}
