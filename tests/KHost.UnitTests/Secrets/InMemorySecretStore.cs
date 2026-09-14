using KHost.Secrets;

namespace KHost.UnitTests.Secrets;

/// <summary>
/// A store that keeps things in a dictionary, keyed the way a real one is.
/// </summary>
/// <remarks>
/// There is no in-process implementation to test against: every real store is the operating
/// system's, which is the point of them. This stands in wherever a test needs a plugin context to
/// exist rather than to exercise storage — the storage itself is proven against the real thing in
/// KHost.IntegrationTests, since that is the only place it can be.
/// </remarks>
internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<(string Service, string Account), string> _values = [];

    public string? Get(string service, string account)
        => _values.TryGetValue((service, account), out var value) ? value : null;

    public void Set(string service, string account, string secret) => _values[(service, account)] = secret;

    public bool Remove(string service, string account) => _values.Remove((service, account));
}
