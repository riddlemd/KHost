using KHost.Secrets;

namespace KHost.Domain.Services.Plugins.Secrets;

/// <summary>Files a plugin secrets under its own id, wherever this machine can keep them.</summary>
/// <remarks>Scoping lives here, not in the store, which knows only a (service, account) pair.</remarks>
public sealed class PluginSecretStore : IPluginSecretStore
{
    private readonly ISecretStore _store;

    public PluginSecretStore(ISecretStore store) => _store = store;

    public Task<string?> ReadAsync(string pluginId, string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.Get(ServiceFor(pluginId), key));

    public Task WriteAsync(string pluginId, string key, string? value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(value))
            _store.Remove(ServiceFor(pluginId), key);
        else
            _store.Set(ServiceFor(pluginId), key, value);

        return Task.CompletedTask;
    }

    /// <summary>What the secret is filed under: the plugin id, not its changeable name.</summary>
    /// <remarks>Legible enough for Keychain Access to show who put it there.</remarks>
    private static string ServiceFor(string pluginId) => $"KHost plugin {pluginId}";
}
