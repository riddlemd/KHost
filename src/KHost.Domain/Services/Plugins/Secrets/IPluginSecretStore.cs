namespace KHost.Domain.Services.Plugins.Secrets;

/// <summary>
/// Where a plugin's secrets actually go. One implementation per way a machine has of keeping one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not in <c>KHost.Abstractions</c>, unlike every other service interface here.</b>
/// The rule exists so a plugin can reach what it needs; this is the one contract a plugin must not
/// reach. It takes a plugin id, so anything holding it can read any plugin's secrets by naming
/// someone else — and a plugin references <c>Abstractions</c> and <c>Common</c>, so leaving the
/// type out of both is what makes that impossible rather than merely discouraged.
/// </para>
/// <para>
/// What a plugin sees is <see cref="Abstractions.Services.IPluginContext"/>, which already knows
/// which plugin it belongs to and fills the id in itself.
/// </para>
/// </remarks>
public interface IPluginSecretStore
{
    /// <summary>The value, or null when nothing was stored under that name.</summary>
    Task<string?> ReadAsync(string pluginId, string key, CancellationToken cancellationToken = default);

    /// <summary>Stores a value, or forgets it when <paramref name="value"/> is null or empty.</summary>
    Task WriteAsync(string pluginId, string key, string? value, CancellationToken cancellationToken = default);
}
