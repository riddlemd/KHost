using KHost.Secrets.Interop;
using KHost.Secrets.Interop.MacOS;

namespace KHost.Secrets;

/// <summary>The login keychain, via the ported MacOSKeychain: a thin adapter.</summary>
/// <remarks>CoreFoundation marshalling and SecItem shapes stay upstream's so fixes stay diffable.</remarks>
public sealed class MacOsSecretStore : ISecretStore
{
    private readonly ICredentialStore _keychain = new MacOSKeychain();

    public string? Get(string service, string account) => _keychain.Get(service, account)?.Password;

    public void Set(string service, string account, string secret)
        => _keychain.AddOrUpdate(service, account, secret);

    public bool Remove(string service, string account) => _keychain.Remove(service, account);
}
