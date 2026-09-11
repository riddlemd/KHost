using KHost.Secrets.Interop;
using KHost.Secrets.Interop.Linux;

namespace KHost.Secrets;

/// <summary>
/// The Secret Service (gnome-keyring, KWallet and friends), through the ported
/// <see cref="SecretServiceCollection"/>.
/// </summary>
/// <remarks>
/// The one backend that may not be there. libsecret is not on every distribution, and the service
/// behind it wants a desktop session — a headless or SSH-only box commonly has neither. That is why
/// <see cref="SecretStores"/> probes this one by using it rather than by asking what platform it is
/// on: on Linux, being able to store a secret is a property of the machine, not of the OS.
/// </remarks>
public sealed class LinuxSecretStore : ISecretStore
{
    private readonly ICredentialStore _credentials = new SecretServiceCollection(@namespace: null);

    public SecretProtection Protection => SecretProtection.OperatingSystem;

    public string? Get(string service, string account) => _credentials.Get(service, account)?.Password;

    public void Set(string service, string account, string secret)
        => _credentials.AddOrUpdate(service, account, secret);

    public bool Remove(string service, string account) => _credentials.Remove(service, account);
}
