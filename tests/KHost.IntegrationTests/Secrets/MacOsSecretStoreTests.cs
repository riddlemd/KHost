using KHost.Secrets;

namespace KHost.IntegrationTests.Secrets;

/// <summary>
/// Drives the real login keychain, which is why this is here and not in the unit suite: the ported
/// interop is the one part of a secret store that cannot be proven with a substitute. A fake would
/// only confirm the adapter forwards its arguments, and the arguments were never the risk —
/// CoreFoundation marshalling and the SecItem result codes are.
/// </summary>
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

        Assert.Equal(SecretProtection.OperatingSystem, store.Protection);
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

    /// <summary>
    /// Removing is what a sign-out depends on, and "there was nothing there" has to be
    /// distinguishable from "it went" — otherwise a caller cannot tell a failed delete from a
    /// no-op.
    /// </summary>
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
