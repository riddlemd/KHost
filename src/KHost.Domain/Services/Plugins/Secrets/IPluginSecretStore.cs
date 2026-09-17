namespace KHost.Domain.Services.Plugins.Secrets;

/// <summary>Where a plugin's secrets actually go.</summary>
/// <remarks>Not in Abstractions: takes a plugin id, so a plugin could read a peer by naming it.</remarks>
public interface IPluginSecretStore
{
    /// <summary>The value, or null when nothing was stored under that name.</summary>
    Task<string?> ReadAsync(string pluginId, string key, CancellationToken cancellationToken = default);

    /// <summary>Stores a value, or forgets it when <paramref name="value"/> is null or empty.</summary>
    Task WriteAsync(string pluginId, string key, string? value, CancellationToken cancellationToken = default);
}
