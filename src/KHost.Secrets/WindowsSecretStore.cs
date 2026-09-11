using KHost.Secrets.Interop;
using KHost.Secrets.Interop.Windows;

namespace KHost.Secrets;

/// <summary>
/// Windows Credential Manager, through the ported <see cref="WindowsCredentialManager"/>.
/// </summary>
/// <remarks>
/// Credential Manager rather than raw DPAPI on purpose: both encrypt under the account's own key,
/// but this one also shows the entry in a place a person can find, inspect and revoke. A venue's
/// operator being able to see what KHost stored is worth more than saving a file.
/// </remarks>
public sealed class WindowsSecretStore : ISecretStore
{
    private readonly ICredentialStore _credentials = new WindowsCredentialManager();

    public SecretProtection Protection => SecretProtection.OperatingSystem;

    public string? Get(string service, string account) => _credentials.Get(service, account)?.Password;

    public void Set(string service, string account, string secret)
        => _credentials.AddOrUpdate(service, account, secret);

    public bool Remove(string service, string account) => _credentials.Remove(service, account);
}
