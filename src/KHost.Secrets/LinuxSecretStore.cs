using KHost.Secrets.Interop;
using KHost.Secrets.Interop.Linux;

namespace KHost.Secrets;

/// <summary>The Secret Service (gnome-keyring, KWallet) via the ported SecretServiceCollection.</summary>
/// <remarks>libsecret is not probed up front; Explain makes the bare loader error actionable.</remarks>
public sealed class LinuxSecretStore : ISecretStore
{
    private readonly ICredentialStore _credentials = new SecretServiceCollection(@namespace: null);

    public string? Get(string service, string account)
        => Explain(() => _credentials.Get(service, account)?.Password);

    public void Set(string service, string account, string secret)
        => Explain<object?>(() => { _credentials.AddOrUpdate(service, account, secret); return null; });

    public bool Remove(string service, string account) => Explain(() => _credentials.Remove(service, account));

    /// <summary>Names what is missing: the bare loader error otherwise just names a .so.</summary>
    private static T Explain<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (DllNotFoundException ex)
        {
            throw new PlatformNotSupportedException(
                "This machine has no libsecret, so KHost has nowhere to keep a credential. "
                + "Install libsecret (libsecret-1-0 on Debian and Ubuntu, libsecret on Fedora and Arch) "
                + "and make sure a Secret Service such as gnome-keyring is running.", ex);
        }
    }
}
