using System.Runtime.InteropServices;

namespace KHost.Abstractions.Models.Plugins;

/// <summary>
/// The platform a release is built for. Deliberately coarser than a .NET RID: a plugin splits by
/// what the OS gives it — WinRT, AppleScript, MPRIS — not by distro, and a catalog full of
/// linux-musl-arm64 spellings is a catalog nobody keeps correct.
/// </summary>
public static class PluginRid
{
    private static readonly string[] KnownPlatforms = ["win", "osx", "linux"];
    private static readonly string[] KnownArchitectures = ["x64", "arm64", "x86", "arm"];

    /// <summary>"win", "osx", "linux", or empty where the host cannot say.</summary>
    public static string Current
        => OperatingSystem.IsWindows() ? "win"
         : OperatingSystem.IsMacOS() ? "osx"
         : OperatingSystem.IsLinux() ? "linux"
         : string.Empty;

    public static string CurrentArchitecture => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        Architecture.Arm => "arm",
        _ => string.Empty,
    };

    /// <summary>
    /// Whether this host can run a release built for <paramref name="rid"/>. Blank means the
    /// release is platform-neutral, which is the common case and the one a plugin should aim for.
    /// </summary>
    public static bool Matches(string? rid)
    {
        if (string.IsNullOrWhiteSpace(rid))
            return true;

        var (platform, architecture) = Split(rid);

        if (!string.Equals(platform, Current, StringComparison.OrdinalIgnoreCase))
            return false;

        // No architecture named means the whole platform, so a win release runs on win-arm64.
        return architecture is null
            || string.Equals(architecture, CurrentArchitecture, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a catalog may publish this rid at all. A spelling the host does not recognise would
    /// match nothing and read as "no build for your platform" on every machine, so it is refused
    /// where the catalog is reviewed rather than left to puzzle a host later.
    /// </summary>
    public static bool IsKnown(string? rid)
    {
        if (string.IsNullOrWhiteSpace(rid))
            return true;

        var (platform, architecture) = Split(rid);

        return KnownPlatforms.Contains(platform, StringComparer.OrdinalIgnoreCase)
            && (architecture is null || KnownArchitectures.Contains(architecture, StringComparer.OrdinalIgnoreCase));
    }

    private static (string Platform, string? Architecture) Split(string rid)
    {
        var parts = rid.Trim().Split('-');

        // Anything beyond platform-architecture is a spelling this host does not model; the empty
        // platform it returns matches nothing and fails IsKnown.
        return parts.Length switch
        {
            1 => (parts[0], null),
            2 => (parts[0], parts[1]),
            _ => (string.Empty, null),
        };
    }
}
