using KHost.Secrets;

namespace KHost.IntegrationTests.Secrets;

/// <summary>Drives the real Credential Manager; a substitute proves only forwarding.</summary>
/// <remarks>There was no test here, and the store did not work at all: every write threw, so a
/// plugin signed in, kept its session in memory for that run, and asked again after every restart.
/// The store underneath is ported from Git Credential Manager, where a service is always a URL —
/// <see cref="ISecretStore"/> takes any string, which is what the macOS store has always been
/// given.</remarks>
public class WindowsSecretStoreTests : IDisposable
{
    // Distinct per run, so a crashed run cannot collide with a later one, and cleanup below has
    // something unambiguous to remove from a developer's real vault.
    private readonly string _service = $"khost-tests-{Guid.NewGuid():N}";

    /// <summary>Shaped like what <c>PluginSecretStore</c> files a plugin's secrets under, which is
    /// the name that threw: spaces, and nothing a Uri can parse.</summary>
    private readonly string _pluginService = $"KHost plugin {Guid.NewGuid()}";

    private const string Account = "session";

    [RequiresCredentialManagerFact]
    public void RoundTrips_ThroughTheRealVault()
    {
        var store = new WindowsSecretStore();

        Assert.Null(store.Get(_service, Account));

        store.Set(_service, Account, "hunter2");

        Assert.Equal("hunter2", store.Get(_service, Account));
    }

    /// <summary>The regression: a plugin's own service name is not a URL, and writing one threw
    /// "Invalid URI: The format of the URI could not be determined" all the way out to the host.</summary>
    [RequiresCredentialManagerFact]
    public void RoundTrips_AServiceNameThatIsNotAUrl()
    {
        var store = new WindowsSecretStore();

        store.Set(_pluginService, Account, "a-session-key");

        Assert.Equal("a-session-key", store.Get(_pluginService, Account));
    }

    /// <summary>A second Set replaces rather than adding a duplicate under the same pair.</summary>
    [RequiresCredentialManagerFact]
    public void Set_Twice_Replaces()
    {
        var store = new WindowsSecretStore();

        store.Set(_pluginService, Account, "first");
        store.Set(_pluginService, Account, "second");

        Assert.Equal("second", store.Get(_pluginService, Account));
    }

    /// <summary>Signing out has to actually drop the credential, or the next start signs back in.</summary>
    [RequiresCredentialManagerFact]
    public void Remove_TakesTheCredentialOut()
    {
        var store = new WindowsSecretStore();
        store.Set(_pluginService, Account, "a-session-key");

        Assert.True(store.Remove(_pluginService, Account));
        Assert.Null(store.Get(_pluginService, Account));
    }

    /// <summary>Two plugins file under their own ids, and neither may read the other's.</summary>
    [RequiresCredentialManagerFact]
    public void Get_DoesNotReachAnotherServicesSecret()
    {
        var store = new WindowsSecretStore();
        var other = $"KHost plugin {Guid.NewGuid()}";

        store.Set(_pluginService, Account, "mine");
        try
        {
            Assert.Null(store.Get(other, Account));
        }
        finally
        {
            store.Remove(other, Account);
        }
    }

    /// <summary>The service name is carried in the path of a URL, so a character that means
    /// something there has to be escaped on the way in.</summary>
    /// <remarks>A '#' left raw starts a fragment, and everything after it stops being part of the
    /// path — so two services differing only past one would be handed each other's secrets.</remarks>
    [RequiresCredentialManagerFact]
    public void Get_DoesNotConflateServicesDifferingOnlyAfterAUriDelimiter()
    {
        var store = new WindowsSecretStore();
        var mine = $"{_pluginService}#one";
        var other = $"{_pluginService}#two";

        store.Set(mine, Account, "mine");
        try
        {
            Assert.Null(store.Get(other, Account));
        }
        finally
        {
            store.Remove(mine, Account);
            store.Remove(other, Account);
        }
    }

    /// <summary>Never leave a test's secret behind in a developer's own vault.</summary>
    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            var store = new WindowsSecretStore();
            store.Remove(_service, Account);
            store.Remove(_pluginService, Account);
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>xUnit 2 cannot skip at runtime, so the decision is made in the constructor.</summary>
public sealed class RequiresCredentialManagerFactAttribute : FactAttribute
{
    public RequiresCredentialManagerFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Credential Manager is Windows only";
    }
}
