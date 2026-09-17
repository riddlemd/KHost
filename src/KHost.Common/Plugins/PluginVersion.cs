namespace KHost.Common.Plugins;

/// <summary>Compares plugin/catalog version strings; <c>PluginManifest.Version</c> is unformatted.</summary>
public static class PluginVersion
{
    /// <summary>A comparable version; unparseable text becomes 0.0.0.0, sorting lowest.</summary>
    /// <remarks>SemVer pre-release/build suffixes are dropped, so 1.2.0-beta compares as 1.2.0.</remarks>
    public static Version Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new Version(0, 0, 0, 0);

        var trimmed = value.Trim().TrimStart('v', 'V');
        var cut = trimmed.IndexOfAny(['-', '+']);

        if (cut >= 0)
            trimmed = trimmed[..cut];

        return Version.TryParse(trimmed, out var parsed) ? Normalize(parsed) : new Version(0, 0, 0, 0);
    }

    /// <summary>True when candidate is strictly newer than installed.</summary>
    public static bool IsNewer(string? candidate, string? installed) => Parse(candidate) > Parse(installed);

    // Version treats absent components as -1, so "1.0" would otherwise not equal "1.0.0".
    private static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
}
