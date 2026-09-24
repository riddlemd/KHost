namespace KHost.Abstractions.Services;

/// <summary>Keeps small state that outlives a restart but does not belong in the database, such as
/// the queue and the selected venue.</summary>
/// <remarks>Host-owned. A plugin can reach it, but keys are one namespace shared with the host's
/// own state, so a plugin key can overwrite the host's; a plugin's settings and secrets belong on
/// <see cref="IPluginContext"/> instead. Values are serialised, so store plain data; one that does
/// not survive the round trip loads as the default. A host singleton, callable from any thread.
/// Announces nothing.</remarks>
public interface ICacheService
{
    /// <summary>Reads back what was saved under <paramref name="key"/>.</summary>
    /// <returns>The default of <typeparamref name="T"/> when nothing is saved, or when what is saved
    /// cannot be read back as <typeparamref name="T"/>; the failure is logged, never thrown.</returns>
    Task<T?> LoadAsync<T>(string key);

    /// <summary>Replaces what is saved under <paramref name="key"/>.</summary>
    /// <remarks>A failed write is logged and swallowed, so a caller cannot tell it failed.</remarks>
    Task SaveAsync<T>(string key, T state);
}
