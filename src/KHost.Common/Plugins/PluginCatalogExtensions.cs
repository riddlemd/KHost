using KHost.Abstractions.Models.Plugins;

namespace KHost.Common.Plugins;

/// <summary>What a host may do with a published catalog entry, apart from the entry itself.</summary>
/// <remarks>A catalog row is a contract; deciding what installs is behaviour.</remarks>
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

    /// <summary>The newest release this host can load, or null when none target its API.</summary>
    /// <remarks>The loader compares versions for equality, not a minimum.</remarks>
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
