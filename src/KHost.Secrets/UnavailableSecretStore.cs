namespace KHost.Secrets;

/// <summary>
/// The store for a machine that has none — today, anything that is not macOS.
/// </summary>
/// <remarks>
/// Drops writes rather than putting them somewhere weaker. A venue that cannot protect a secret
/// should be asked for one, not quietly have it written to a file that looked like a keychain;
/// <see cref="Protection"/> says so before a caller decides to store anything.
/// </remarks>
public sealed class UnavailableSecretStore : ISecretStore
{
    public SecretProtection Protection => SecretProtection.None;

    public string? Get(string service, string account) => null;

    public void Set(string service, string account, string secret) { }

    public bool Remove(string service, string account) => false;
}
