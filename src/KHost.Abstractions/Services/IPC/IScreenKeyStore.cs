namespace KHost.Abstractions.Services.IPC;

/// <summary>
/// The host's record of which key authenticates which screen. A screen is provisioned a key when it
/// is launched; the hub verifies every message from that screen against the key. A screen with no
/// key here cannot be trusted and is refused — which is also why a peer that never received a key
/// (an unprovisioned LAN stranger) cannot connect.
/// </summary>
public interface IScreenKeyStore
{
    /// <summary>Generates a fresh key for a screen, persists it, and returns the file path to hand the screen.</summary>
    string Provision(string screenId);

    /// <summary>The screen's key bytes, or null if it has none.</summary>
    byte[]? GetKey(string screenId);

    /// <summary>Forgets a screen's key — called when the host closes a screen it launched.</summary>
    void Revoke(string screenId);
}
