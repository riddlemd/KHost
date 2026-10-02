using KHost.Secrets.Interop;
using KHost.Secrets.Interop.Linux;

namespace KHost.Secrets;

/// <summary>The Secret Service (gnome-keyring, KWallet) via the ported SecretServiceCollection.</summary>
/// <remarks>libsecret is not probed up front; Explain makes the bare loader and keyring errors actionable.</remarks>
public sealed class LinuxSecretStore : ISecretStore
{
    private readonly ICredentialStore _credentials = new SecretServiceCollection(@namespace: null);

    public string? Get(string service, string account)
        => Explain(() => _credentials.Get(service, account)?.Password);

    public void Set(string service, string account, string secret)
        => Explain<object?>(() => { _credentials.AddOrUpdate(service, account, secret); return null; });

    public bool Remove(string service, string account) => Explain(() => _credentials.Remove(service, account));

    /// <summary>Names what is missing: the bare loader error otherwise just names a .so.</summary>
    internal static T Explain<T>(Func<T> operation)
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
        catch (InteropException ex)
        {
            // libsecret loaded but no keyring answered (none on the bus, or locked after an auto-login).
            // The interop's text names only the failed call; GLib's reason is the inner exception.
            throw new PlatformNotSupportedException(
                "No unlocked keyring, so this is forgotten when KHost restarts. "
                + "Start or unlock gnome-keyring or KWallet. "
                + $"({ex.InnerException?.Message ?? ex.Message})", ex);
        }
    }
}
