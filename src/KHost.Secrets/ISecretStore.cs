namespace KHost.Secrets;

/// <summary>Somewhere a small string can be kept that a file should not hold.</summary>
/// <remarks>Knows nothing of plugins or venues: just a secret under a (service, account) pair.</remarks>
public interface ISecretStore
{
    /// <summary>The stored secret, or null when nothing is filed under that pair.</summary>
    string? Get(string service, string account);

    /// <summary>Stores a secret, replacing any already filed under the same pair.</summary>
    void Set(string service, string account, string secret);

    /// <summary>Forgets a secret. Returns false when there was nothing to forget.</summary>
    bool Remove(string service, string account);
}
