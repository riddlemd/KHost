using KHost.Secrets.Interop;
using KHost.Secrets.Interop.Linux;

namespace KHost.Secrets;

/// <summary>
/// The Secret Service (gnome-keyring, KWallet and friends), through the ported
/// <see cref="SecretServiceCollection"/>.
/// </summary>
/// <remarks>
/// The one backend that may not be there: libsecret is not on every distribution, and the service
/// behind it wants a session bus that a headless box commonly has not got. Neither is checked for
/// up front — a console that never touches a secret should still start — so the failure lands on
/// first use, where <see cref="Explain"/> turns the runtime's bare loader error into something a
/// venue can act on.
/// </remarks>
public sealed class LinuxSecretStore : ISecretStore
{
    private readonly ICredentialStore _credentials = new SecretServiceCollection(@namespace: null);

    public string? Get(string service, string account)
        => Explain(() => _credentials.Get(service, account)?.Password);

    public void Set(string service, string account, string secret)
        => Explain<object?>(() => { _credentials.AddOrUpdate(service, account, secret); return null; });

    public bool Remove(string service, string account) => Explain(() => _credentials.Remove(service, account));

    /// <summary>
    /// Names what is missing. Without this the failure is a bare loader error naming a .so, which
    /// tells a venue nothing about what to install or why KHost suddenly wants a password.
    /// </summary>
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
