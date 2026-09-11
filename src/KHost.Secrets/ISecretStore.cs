namespace KHost.Secrets;

/// <summary>
/// Somewhere a small string can be kept that a file should not hold. Deliberately knows nothing
/// about plugins, venues or anything else in KHost: what it stores is a secret belonging to a
/// (service, account) pair, and who those name is the caller's business.
/// </summary>
public interface ISecretStore
{
    /// <summary>What this machine can actually promise about a stored value.</summary>
    SecretProtection Protection { get; }

    /// <summary>The stored secret, or null when nothing is filed under that pair.</summary>
    string? Get(string service, string account);

    /// <summary>Stores a secret, replacing any already filed under the same pair.</summary>
    void Set(string service, string account, string secret);

    /// <summary>Forgets a secret. Returns false when there was nothing to forget.</summary>
    bool Remove(string service, string account);
}

/// <summary>
/// How well a machine can keep a secret. Carried so a caller can decide whether to store one at
/// all rather than be told afterwards that it went nowhere.
/// </summary>
public enum SecretProtection
{
    /// <summary>Nothing is kept: writes are dropped and reads answer null.</summary>
    None,

    /// <summary>The operating system's own store, encrypted at rest under this account's key.</summary>
    OperatingSystem,
}
