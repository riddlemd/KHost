using KHost.Secrets.Interop;
using KHost.Secrets.Interop.Windows;

namespace KHost.Secrets;

/// <summary>Windows Credential Manager, via the ported WindowsCredentialManager.</summary>
/// <remarks>Chosen over raw DPAPI: the entry is somewhere a person can find, inspect and revoke.</remarks>
public sealed class WindowsSecretStore : ISecretStore
{
    private readonly ICredentialStore _credentials = new WindowsCredentialManager();

    public string? Get(string service, string account) => _credentials.Get(service, account)?.Password;

    public void Set(string service, string account, string secret)
        => _credentials.AddOrUpdate(service, account, secret);

    public bool Remove(string service, string account) => _credentials.Remove(service, account);
}
