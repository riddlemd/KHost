using KHost.Abstractions.Models.Plugins;

namespace KHost.Common.Plugins;

/// <summary>What a host may do with a published catalog entry, apart from the entry itself.</summary>
/// <remarks>A catalog row is a contract; deciding what installs is behaviour.</remarks>
public static class PluginCatalogExtensions
{
    /// <summary>True when some release was built against a plugin API this host runs, whatever else
    /// is wrong with it. Tells "built for another KHost" apart from the other reasons nothing
    /// installs.</summary>
    public static bool HasReleaseForThisHost(this PluginCatalogEntry entry)
        => entry.Releases.Exists(release => PluginApiRange.ThisHost.Covers(release.ApiVersion));

    /// <summary>True when some release runs on this host's plugin API range *and* its platform.</summary>
    public static bool HasReleaseForThisPlatform(this PluginCatalogEntry entry)
        => entry.Releases.Exists(release => PluginApiRange.ThisHost.Covers(release.ApiVersion)
                                   && PluginRid.MatchesThisHost(release.Rid));

    /// <summary>Why no release runs on this host's plugin API range, judged by the release built
    /// against the newest plugin API: whether KHost or the plugin needs the update. Null when some
    /// release is in range, or when there are no releases.</summary>
    public static string? DescribeApiRefusal(this PluginCatalogEntry entry)
    {
        if (entry.Releases.Count == 0 || entry.HasReleaseForThisHost())
            return null;

        return PluginApiRange.ThisHost.DescribeRefusal(entry.Releases.Max(release => release.ApiVersion));
    }

    /// <summary>The newest plugin version this host can install, or null when none is built against
    /// a plugin API in this host's range for this platform.</summary>
    public static PluginCatalogRelease? LatestCompatibleRelease(this PluginCatalogEntry entry)
        => entry.LatestCompatibleRelease(PluginApiRange.ThisHost);

    /// <summary>The newest plugin version a host running <paramref name="range"/> can install.</summary>
    public static PluginCatalogRelease? LatestCompatibleRelease(this PluginCatalogEntry entry, PluginApiRange range)
        => entry.Releases
            .Where(release => range.Covers(release.ApiVersion)
                           && release.IsInstallable
                           && PluginRid.MatchesThisHost(release.Rid))
            .OrderByDescending(release => PluginVersion.Parse(release.Version))
            // Version first, platform second: a newer neutral build beats an older one built for
            // this OS, and at the same version the platform build is the more capable package.
            .ThenByDescending(release => string.IsNullOrWhiteSpace(release.Rid) ? 0 : 1)
            .FirstOrDefault();
}
