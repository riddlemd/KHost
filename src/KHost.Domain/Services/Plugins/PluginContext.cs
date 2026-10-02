using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Plugins.Secrets;
using System.Text.Json;
using KHost.Domain.Services.QrCodes;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Plugins;

public class PluginContext : IPluginContext
{
    private readonly Dictionary<string, JsonElement> _values;
    private readonly Dictionary<string, JsonElement> _defaults;
    private readonly DiscoveredPlugin _plugin;
    private readonly IPluginSecretStore _secrets;
    private readonly IQrCodeService _qrCodes;
    private readonly IMessageBroker _broker;
    private readonly IFlashService _flash;
    private readonly ILogger<PluginContext> _logger;
    private readonly Dictionary<int, AddedWarning> _added = [];
    private int _lastWarningId;
    private readonly string _pluginId;

    public PluginContext(
        PluginManifest manifest,
        Dictionary<string, JsonElement>? storedValues,
        DiscoveredPlugin plugin,
        IPluginSecretStore secrets,
        IQrCodeService qrCodes,
        IMessageBroker broker,
        IFlashService flash,
        ILogger<PluginContext> logger)
    {
        _broker = broker;
        _plugin = plugin;
        _secrets = secrets;
        _qrCodes = qrCodes;
        _flash = flash;
        _logger = logger;

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

    public Task RegisterQrCodeAsync(string payload, string? caption = null, CancellationToken cancellationToken = default)
        // Same _pluginId the secrets are filed under, and for the same reason: the owner is the
        // host's to say. Whether this reaches a screen is the venue's call, made later.
        => _qrCodes.RegisterAsync(new QrCodeRegistration
        {
            OwnerId = _pluginId,
            Payload = payload,
            Caption = caption,
        });

    public Task UnregisterQrCodeAsync(CancellationToken cancellationToken = default)
        => _qrCodes.UnregisterAsync(_pluginId);

    /// <remarks>Logged and flashed as well as listed: startup's dump of the list has already run by
    /// the time a sign-in fails, and the Plugins page is not where the host is looking.</remarks>
    public int AddWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return 0;

        int id;
        var changed = false;

        lock (_plugin.Warnings)
        {
            var existing = _added.FirstOrDefault(pair => pair.Value.Message == message);

            if (existing.Key != 0)
                return existing.Key;

            id = ++_lastWarningId;

            // A line the host already shows is not shown twice, and stays the host's to keep:
            // clearing this id must not take it.
            var owned = !_plugin.Warnings.Contains(message);

            if (owned)
            {
                _plugin.Warnings.Add(message);
                changed = true;
            }

            _added[id] = new AddedWarning(message, owned);
        }

        if (changed)
        {
            _logger.LogWarning("Plugin {Name}: {Warning}", _plugin.DisplayName, message);
            _flash.Show($"{_plugin.DisplayName}: {message}", FlashType.Warning);
            _broker.Announce(new PluginsChanged());
        }

        return id;
    }

    public void ClearWarning(int id)
    {
        bool changed;

        lock (_plugin.Warnings)
            changed = Remove(id);

        if (changed)
            _broker.Announce(new PluginsChanged());
    }

    public void ClearWarnings()
    {
        var changed = false;

        lock (_plugin.Warnings)
        {
            foreach (var id in _added.Keys.ToList())
                changed |= Remove(id);
        }

        if (changed)
            _broker.Announce(new PluginsChanged());
    }

    // Caller holds the lock. True when a line left the list.
    private bool Remove(int id)
    {
        if (!_added.Remove(id, out var warning)) return false;

        return warning.Owned && _plugin.Warnings.Remove(warning.Message);
    }

    private readonly record struct AddedWarning(string Message, bool Owned);
}
