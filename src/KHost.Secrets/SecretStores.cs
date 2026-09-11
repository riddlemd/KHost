namespace KHost.Secrets;

/// <summary>Picks the store for the platform this is running on.</summary>
public static class SecretStores
{
    /// <summary>
    /// The keychain on macOS, Credential Manager on Windows, the Secret Service on Linux.
    /// </summary>
    /// <remarks>
    /// There is no store for a machine that has nowhere to keep a secret, because there is no
    /// useful behaviour for one: a caller that cannot store a credential cannot do the thing it
    /// wanted the credential for, and quietly dropping it only moves the failure somewhere less
    /// obvious. A platform without a store is a deployment that needs fixing, and this says so.
    ///
    /// Linux is the one where that can be true of the machine rather than the OS — libsecret is
    /// not on every distribution. It is not probed for here, because a console that will never
    /// touch a secret should still start; <see cref="LinuxSecretStore"/> reports it on first use
    /// instead, when it is a real problem rather than a hypothetical one.
    /// </remarks>
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
