using System.Runtime.InteropServices;

namespace KHost.Secrets;

/// <summary>Picks the store this machine can actually use.</summary>
public static class SecretStores
{
    /// <summary>
    /// The keychain on macOS, Credential Manager on Windows, the Secret Service on Linux — and
    /// <see cref="UnavailableSecretStore"/> when the machine offers none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Linux is the only one that has to be checked rather than assumed. macOS and Windows carry
    /// their store in the OS; a Linux box may have no libsecret at all, so the library is probed
    /// for before the store claims it can keep anything.
    /// </para>
    /// <para>
    /// The probe loads the library and stops there, deliberately: asking the Secret Service a real
    /// question can raise an unlock prompt, and a karaoke console putting a password dialog on the
    /// screen at startup is worse than the thing being guarded against. What it therefore cannot
    /// rule out is libsecret present with no session bus behind it — common on a headless box —
    /// which surfaces as a throw on first use rather than here. That is the intended failure: a
    /// credential that cannot be kept must be reported, never quietly dropped.
    /// </para>
    /// </remarks>
    public static ISecretStore ForThisMachine()
    {
        if (OperatingSystem.IsMacOS())
            return new MacOsSecretStore();

        if (OperatingSystem.IsWindows())
            return new WindowsSecretStore();

        if (OperatingSystem.IsLinux() && HasLibsecret())
            return new LinuxSecretStore();

        return new UnavailableSecretStore();
    }

    private static bool HasLibsecret()
    {
        if (NativeLibrary.TryLoad("libsecret-1.so.0", out var handle))
        {
            NativeLibrary.Free(handle);
            return true;
        }

        return false;
    }
}
