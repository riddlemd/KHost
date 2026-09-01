using KHost.Abstractions.Models.Plugins;

namespace KHost.Common.Plugins;

/// <summary>
/// What a host may do with a published catalog entry. Apart from the entry itself because a
/// catalog row is a contract — a plugin and the sync tool both bind to it — while deciding what
/// this host can install is behaviour.
/// </summary>
public static class PluginCatalogExtensions
{
    /// <summary>True when some release targets this host's plugin API, whatever else is wrong
    /// with it. Tells "built for another KHost" apart from the other reasons nothing installs.</summary>
    public static bool HasReleaseForThisHost(this PluginCatalogEntry entry)
        => entry.Releases.Exists(release => release.ApiVersion == PluginApi.CurrentVersion);

    /// <summary>True when some release targets this host's plugin API *and* its platform.</summary>
    public static bool HasReleaseForThisPlatform(this PluginCatalogEntry entry)
        => entry.Releases.Exists(release => release.ApiVersion == PluginApi.CurrentVersion
                                   && PluginRid.MatchesThisHost(release.Rid));

    /// <summary>
    /// The newest release this host can actually load, or null when every release targets a
    /// different plugin API. The loader compares API versions for equality, not a minimum, so a
    /// mismatch here would install cleanly and then sit as Incompatible after the restart.
    /// </summary>
    public static PluginCatalogRelease? LatestCompatibleRelease(this PluginCatalogEntry entry)
        => entry.Releases
            .Where(release => release.ApiVersion == PluginApi.CurrentVersion
                           && release.IsInstallable
                           && PluginRid.MatchesThisHost(release.Rid))
            .OrderByDescending(release => PluginVersion.Parse(release.Version))
            // Version first, platform second: a newer neutral build beats an older one built for
            // this OS, and at the same version the platform build is the more capable package.
            .ThenByDescending(release => string.IsNullOrWhiteSpace(release.Rid) ? 0 : 1)
            .FirstOrDefault();
}
