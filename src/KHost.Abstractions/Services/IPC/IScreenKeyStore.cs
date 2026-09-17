namespace KHost.Abstractions.Services.IPC;

/// <summary>Which key authenticates which screen; keyless is refused, keeping LAN strangers out.</summary>
public interface IScreenKeyStore
{
    /// <summary>Generates and persists a fresh key, returning the file path to hand the screen.</summary>
    string Provision(string screenId);

    /// <summary>The screen's key bytes, or null if it has none.</summary>
    byte[]? GetKey(string screenId);

    /// <summary>Forgets a screen's key; called when the host closes a screen it launched.</summary>
    void Revoke(string screenId);
}
