using KHost.Secrets.Interop;
using KHost.Secrets.Interop.Windows;

namespace KHost.Secrets;

/// <summary>Windows Credential Manager, via the ported WindowsCredentialManager.</summary>
/// <remarks>Chosen over raw DPAPI: the entry is somewhere a person can find, inspect and revoke.</remarks>
public sealed class WindowsSecretStore : ISecretStore
{
    /// <summary>Scheme and authority the service name hangs off, so the store below gets the URL
    /// it expects. "khost" rather than "https" so nothing reads a vault entry as a real address.
    /// </summary>
    internal const string TargetPrefix = "khost://secret/";

    private readonly ICredentialStore _credentials = new WindowsCredentialManager();

    public string? Get(string service, string account)
        => _credentials.Get(TargetFor(service), account)?.Password;

    public void Set(string service, string account, string secret)
        => _credentials.AddOrUpdate(TargetFor(service), account, secret);

    public bool Remove(string service, string account) => _credentials.Remove(TargetFor(service), account);

    /// <summary>Carries an arbitrary service name across to a store that only speaks URLs.</summary>
    /// <remarks><see cref="ISecretStore"/> takes any string — a plugin's secrets are filed under
    /// "KHost plugin &lt;id&gt;" — while the ported <see cref="WindowsCredentialManager"/> is from
    /// Git Credential Manager, where a service is always a URL and the target name is built by
    /// parsing it. Handed a name with a space in it, it threw before writing anything, so every
    /// secret this machine was asked to keep was silently dropped. Escaped because the name goes
    /// in the path, and a raw space makes a target name that will not parse back.</remarks>
    private static string TargetFor(string service) => TargetPrefix + Uri.EscapeDataString(service);
}
