using KHost.Secrets;

namespace KHost.UnitTests.Secrets;

/// <summary>A store that keeps things in a dictionary, keyed the way a real one is.</summary>
/// <remarks>Every real store is the operating system's; proven for real in KHost.IntegrationTests.</remarks>
internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<(string Service, string Account), string> _values = [];

    public string? Get(string service, string account)
        => _values.TryGetValue((service, account), out var value) ? value : null;

    public void Set(string service, string account, string secret) => _values[(service, account)] = secret;

    public bool Remove(string service, string account) => _values.Remove((service, account));
}
