namespace KHost.Abstractions.Services.IPC;

/// <summary>Which key authenticates which screen; keyless is refused, keeping LAN strangers out.</summary>
/// <remarks>Host-only: part of the wire contract between the host and its own LocalScreen app. A
/// plugin has no business with it; a plugin display authenticates to its own device however that
/// device requires, behind <see cref="IDisplayProvider"/>. A singleton, safe from any thread.</remarks>
public interface IScreenKeyStore
{
    /// <summary>Generates and persists a fresh key, returning the file path to hand the screen.</summary>
    /// <remarks>Replaces any key the screen id already had, so a screen holding the old one is
    /// refused the next time it registers.</remarks>
    string Provision(string screenId);

    /// <summary>The screen's key bytes, or null if it has none.</summary>
    /// <remarks>Also null when the stored key cannot be read, which refuses the screen rather than
    /// failing the host.</remarks>
    byte[]? GetKey(string screenId);

    /// <summary>Forgets a screen's key; called when the host closes a screen it launched.</summary>
    /// <remarks>A no-op for an id with no key.</remarks>
    void Revoke(string screenId);
}
