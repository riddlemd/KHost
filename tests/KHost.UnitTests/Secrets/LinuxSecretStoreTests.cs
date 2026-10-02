using KHost.Secrets;
using KHost.Secrets.Interop;

namespace KHost.UnitTests.Secrets;

public class LinuxSecretStoreTests
{
    /// <summary>libsecret loaded but nothing on the bus answered: the interop names only the call
    /// that failed, which tells a host nothing about what to install or start.</summary>
    [Fact]
    public void Explain_NoKeyringAnswering_SaysWhatToStart_AndKeepsGLibsReason()
    {
        var glib = new InvalidOperationException("The name org.freedesktop.secrets was not provided by any .service files");
        var interop = new InteropException("Failed to open secret service session", 2, glib);

        var thrown = Assert.Throws<PlatformNotSupportedException>(
            () => LinuxSecretStore.Explain<object?>(() => throw interop));

        Assert.Contains("No unlocked keyring", thrown.Message);
        Assert.Contains("gnome-keyring", thrown.Message);
        Assert.Contains("org.freedesktop.secrets was not provided", thrown.Message);
        Assert.Same(interop, thrown.InnerException);
    }

    [Fact]
    public void Explain_NoLibsecret_StillNamesTheLibrary()
    {
        var thrown = Assert.Throws<PlatformNotSupportedException>(
            () => LinuxSecretStore.Explain<object?>(() => throw new DllNotFoundException("libsecret-1.so.0")));

        Assert.Contains("no libsecret", thrown.Message);
    }
}
