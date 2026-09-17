namespace KHost.Secrets;

/// <summary>Picks the store for the platform this is running on.</summary>
public static class SecretStores
{
    /// <summary>The keychain on macOS, Credential Manager on Windows, the Secret Service on Linux.</summary>
    /// <remarks>No store otherwise: silently dropping a credential only hides the failure.</remarks>
    public static ISecretStore ForThisMachine()
    {
        if (OperatingSystem.IsMacOS())
            return new MacOsSecretStore();

        if (OperatingSystem.IsWindows())
            return new WindowsSecretStore();

        if (OperatingSystem.IsLinux())
            return new LinuxSecretStore();

        throw new PlatformNotSupportedException(
            "KHost has no secret store for this platform, and will not keep a credential without one.");
    }
}
