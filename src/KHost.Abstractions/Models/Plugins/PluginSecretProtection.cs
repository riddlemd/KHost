namespace KHost.Abstractions.Models.Plugins;

/// <summary>
/// How well a machine can actually keep a plugin's secret. Readable so a plugin can decide whether
/// to store one at all, and so the Plugins page can say which it is rather than implying a
/// guarantee the machine cannot make.
/// </summary>
public enum PluginSecretProtection
{
    /// <summary>
    /// Nothing is kept. Either the host was built without a store or the operator turned it off:
    /// a write is dropped and a read answers null, so a plugin has to ask a person every time.
    /// </summary>
    None,

    /// <summary>
    /// A file only this account can open. Better than the ordinary cache, and honest about its
    /// limit: anything running as this user reads it, and so does anyone holding the disk.
    /// </summary>
    File,

    /// <summary>
    /// The operating system's own store — Keychain, DPAPI, Secret Service. The secret is encrypted
    /// at rest under the account's own key rather than merely hidden behind file permissions.
    /// </summary>
    OperatingSystem,
}
