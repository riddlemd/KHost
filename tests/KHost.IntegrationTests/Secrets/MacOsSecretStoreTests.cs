using KHost.Secrets;

namespace KHost.IntegrationTests.Secrets;

/// <summary>Drives the real keychain; a substitute proves only forwarding, not CF marshalling.</summary>
public class MacOsSecretStoreTests : IDisposable
{
    // Distinct per run, so a crashed run cannot collide with a later one, and cleanup below has
    // something unambiguous to remove from a developer's real keychain.
    private readonly string _service = $"khost-tests-{Guid.NewGuid():N}";
    private const string Account = "tests@khost.invalid";

    [RequiresKeychainFact]
    public void RoundTrips_ThroughTheRealKeychain()
    {
        var store = new MacOsSecretStore();

        Assert.Null(store.Get(_service, Account));

        store.Set(_service, Account, "hunter2");

        Assert.Equal("hunter2", store.Get(_service, Account));
    }

    /// <summary>A second Set replaces rather than adding a duplicate item under the same pair.</summary>
    [RequiresKeychainFact]
    public void Set_Twice_Replaces()
    {
        var store = new MacOsSecretStore();

        store.Set(_service, Account, "first");
        store.Set(_service, Account, "second");

        Assert.Equal("second", store.Get(_service, Account));
    }

    /// <summary>A caller must tell "nothing was there" from "it went"; sign-out depends on it.</summary>
    [RequiresKeychainFact]
    public void Remove_ReportsWhetherThereWasAnything()
    {
        var store = new MacOsSecretStore();
        store.Set(_service, Account, "hunter2");

        Assert.True(store.Remove(_service, Account));
        Assert.Null(store.Get(_service, Account));
        Assert.False(store.Remove(_service, Account));
    }

    /// <summary>Two services do not see each other's secrets, which is the whole isolation claim.</summary>
    [RequiresKeychainFact]
    public void Services_AreIsolatedFromEachOther()
    {
        var store = new MacOsSecretStore();
        var other = $"{_service}-other";

        store.Set(_service, Account, "mine");
        try
        {
            Assert.Null(store.Get(other, Account));
        }
        finally
        {
            store.Remove(other, Account);
        }
    }

    /// <summary>Never leave a test's secret behind in a developer's own keychain.</summary>
    public void Dispose()
    {
        if (OperatingSystem.IsMacOS())
            new MacOsSecretStore().Remove(_service, Account);

        GC.SuppressFinalize(this);
    }
}

/// <summary>xUnit 2 cannot skip at runtime, so the decision is made in the constructor.</summary>
public sealed class RequiresKeychainFactAttribute : FactAttribute
{
    public RequiresKeychainFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
            Skip = "the keychain store is macOS only";
    }
}
