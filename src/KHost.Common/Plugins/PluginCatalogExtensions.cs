using KHost.Abstractions.Models.Plugins;

namespace KHost.Common.Plugins;

/// <summary>What a host may do with a published catalog entry, apart from the entry itself.</summary>
/// <remarks>A catalog row is a contract; deciding what installs is behaviour.</remarks>
public static class PluginCatalogExtensions
{
    /// <summary>The contracts a release needs: its <see cref="PluginCatalogRelease.ContractsVersion"/>
    /// when it has one, else what its legacy <see cref="PluginCatalogRelease.ApiVersion"/> means.
    /// Null when neither names contracts a host reading this can judge.</summary>
    public static ContractsVersion? RequiredContracts(this PluginCatalogRelease release)
    {
        if (release.ContractsVersion is not null)
            return ContractsVersion.TryParse(release.ContractsVersion, out var version) ? version : null;

        return PluginRequirements.FromLegacyApiVersion(release.ApiVersion);
    }

    /// <summary>True when some release needs contracts this host has, whatever else is wrong with
    /// it. Tells "built for another KHost" apart from the other reasons nothing installs.</summary>
    public static bool HasReleaseForThisHost(this PluginCatalogEntry entry)
        => entry.Releases.Exists(release => RunsOn(release, ContractsVersion.ThisHost));

    /// <summary>True when some release needs contracts this host has *and* runs on its platform.</summary>
    public static bool HasReleaseForThisPlatform(this PluginCatalogEntry entry)
        => entry.Releases.Exists(release => RunsOn(release, ContractsVersion.ThisHost)
                                   && PluginRid.MatchesThisHost(release.Rid));

    /// <summary>Why no release needs contracts this host has, judged by the release needing the
    /// newest: whether KHost or the plugin needs the update. Null when some release runs here, or
    /// when there are no releases.</summary>
    public static string? DescribeApiRefusal(this PluginCatalogEntry entry)
    {
        if (entry.Releases.Count == 0 || entry.HasReleaseForThisHost())
            return null;

        var newest = entry.Releases
            .Select(release => release.RequiredContracts())
            .Where(required => required is not null)
            .Max();

        // No release names contracts a host can read: word it from the newest legacy number.
        return ContractsVersion.ThisHost.DescribeRefusal(newest, newest is null ? entry.Releases.Max(release => release.ApiVersion) : null);
    }

    /// <summary>The newest plugin version this host can install, or null when none needs contracts
    /// this host has, for this platform.</summary>
    public static PluginCatalogRelease? LatestCompatibleRelease(this PluginCatalogEntry entry)
        => entry.LatestCompatibleRelease(ContractsVersion.ThisHost);

    /// <summary>The newest plugin version a host with <paramref name="host"/> contracts can install.</summary>
    public static PluginCatalogRelease? LatestCompatibleRelease(this PluginCatalogEntry entry, ContractsVersion host)
        => entry.Releases
            .Where(release => RunsOn(release, host)
                           && release.IsInstallable
                           && PluginRid.MatchesThisHost(release.Rid))
            .OrderByDescending(release => PluginVersion.Parse(release.Version))
            // Version first, platform second: a newer neutral build beats an older one built for
            // this OS, and at the same version the platform build is the more capable package.
            .ThenByDescending(release => string.IsNullOrWhiteSpace(release.Rid) ? 0 : 1)
            .FirstOrDefault();

    private static bool RunsOn(PluginCatalogRelease release, ContractsVersion host)
        => release.RequiredContracts() is { } required && host.Covers(required);
}
