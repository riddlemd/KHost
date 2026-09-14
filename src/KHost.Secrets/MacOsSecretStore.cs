using KHost.Secrets.Interop;
using KHost.Secrets.Interop.MacOS;

namespace KHost.Secrets;

/// <summary>
/// The login keychain, through the ported <see cref="MacOSKeychain"/>.
/// </summary>
/// <remarks>
/// A thin adapter on purpose. Everything hard here — the CoreFoundation marshalling, the
/// SecItem query shapes, the not-found result code — is upstream's and stays untouched so a later
/// fix can be diffed in; this only narrows a credential store to the one thing KHost wants from
/// it, which is a string under a name.
/// </remarks>
public sealed class MacOsSecretStore : ISecretStore
{
    private readonly ICredentialStore _keychain = new MacOSKeychain();

    public string? Get(string service, string account) => _keychain.Get(service, account)?.Password;

    public void Set(string service, string account, string secret)
        => _keychain.AddOrUpdate(service, account, secret);

    public bool Remove(string service, string account) => _keychain.Remove(service, account);
}
