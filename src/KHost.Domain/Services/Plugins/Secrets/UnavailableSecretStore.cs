using KHost.Abstractions.Models.Plugins;

namespace KHost.Domain.Services.Plugins.Secrets;

/// <summary>
/// The store for a machine that has none. Writes go nowhere and reads answer null.
/// </summary>
/// <remarks>
/// Silent on purpose at this level, and loud at the one above: a plugin is told
/// <see cref="PluginSecretProtection.None"/> before it decides to store anything, so nothing here
/// has to fail a call to make the point. Dropping the value rather than putting it somewhere
/// weaker is the whole point — a venue that cannot protect a credential should be asked for one,
/// not quietly have it written to a file that looked like a keychain.
/// </remarks>
public sealed class UnavailableSecretStore : IPluginSecretStore
{
    public PluginSecretProtection Protection => PluginSecretProtection.None;

    public Task<string?> ReadAsync(string pluginId, string key, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task WriteAsync(string pluginId, string key, string? value, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
