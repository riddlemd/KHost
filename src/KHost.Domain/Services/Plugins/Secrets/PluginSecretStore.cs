using KHost.Abstractions.Models.Plugins;
using KHost.Secrets;

namespace KHost.Domain.Services.Plugins.Secrets;

/// <summary>
/// Files a plugin's secrets under its own id, on whatever this machine can actually keep them in.
/// </summary>
/// <remarks>
/// The scoping lives here rather than in <see cref="ISecretStore"/> because the store has no idea
/// what a plugin is and should not gain one: it keeps a string under a (service, account) pair,
/// and this is what decides that the service is a plugin.
/// </remarks>
public sealed class PluginSecretStore : IPluginSecretStore
{
    private readonly ISecretStore _store;

    public PluginSecretStore(ISecretStore store) => _store = store;

    // Mapped rather than shared: the enum a plugin reads lives in Abstractions, which references
    // nothing at all, so it cannot be the same type the secrets project uses.
    public PluginSecretProtection Protection => _store.Protection switch
    {
        SecretProtection.OperatingSystem => PluginSecretProtection.OperatingSystem,
        _ => PluginSecretProtection.None,
    };

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

    /// <summary>
    /// What the secret is filed under. Carries the plugin's id rather than its name, which a
    /// manifest may change, and reads legibly in whatever the operating system shows a person —
    /// somebody looking at Keychain Access should be able to tell what put it there.
    /// </summary>
    private static string ServiceFor(string pluginId) => $"KHost plugin {pluginId}";
}
