namespace KHost.Common.Plugins;

/// <summary>
/// Comparing the version strings plugins and catalogs publish. <c>PluginManifest.Version</c> is a
/// bare string with no enforced format, so parsing has to survive whatever a publisher wrote.
/// </summary>
public static class PluginVersion
{
    /// <summary>
    /// A comparable version, or 0.0.0.0 for anything unparseable — which sorts an unreadable
    /// version below every readable one rather than throwing on the page that renders it.
    /// SemVer pre-release and build suffixes are dropped, so 1.2.0-beta compares as 1.2.0.
    /// </summary>
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

    /// <summary>True when <paramref name="candidate"/> is strictly newer than <paramref name="installed"/>.</summary>
    public static bool IsNewer(string? candidate, string? installed) => Parse(candidate) > Parse(installed);

    // Version treats absent components as -1, so "1.0" would otherwise not equal "1.0.0".
    private static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
}
