using System.Runtime.InteropServices;

namespace KHost.Common.Plugins;

/// <summary>The platform a release is built for; deliberately coarser than a .NET RID.</summary>
/// <remarks>A plugin splits by what the OS gives it, not by distro.</remarks>
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

    /// <summary>"x64", "arm64", "x86", "arm", or empty where the host cannot say.</summary>
    public static string CurrentArchitecture => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        Architecture.Arm => "arm",
        _ => string.Empty,
    };

    /// <summary>Whether this host runs a build for <paramref name="rid"/>; blank is neutral.</summary>
    public static bool MatchesThisHost(string? rid)
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

    /// <summary>Whether a catalog may publish this rid at all. A spelling the host cannot parse
    /// matches nothing, so it reads as unavailable on every machine.</summary>
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
