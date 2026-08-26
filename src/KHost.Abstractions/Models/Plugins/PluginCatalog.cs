using KHost.Plugins.Sdk;

namespace KHost.Abstractions.Models.Plugins;

/// <summary>
/// The published list of installable plugins, fetched as a static JSON document. Deliberately all
/// optional-with-defaults rather than <c>required</c>: this is parsed from a remote file, so a
/// missing field has to degrade one entry, not throw away the whole catalog.
/// </summary>
public sealed class PluginCatalog
{
    /// <summary>Catalogs the host does not recognise are rejected whole — see <see cref="SupportedSchemaVersion"/>.</summary>
    public const int SupportedSchemaVersion = 1;

    public int SchemaVersion { get; set; }

    public List<PluginCatalogEntry> Plugins { get; set; } = [];
}

public sealed class PluginCatalogEntry
{
    /// <summary>Matches the plugin's own <c>manifest.json</c> id, which is how an installed row and
    /// a catalog row are recognised as the same plugin.</summary>
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Author { get; set; }

    public string? Description { get; set; }

    /// <summary>Where the source lives, so a host can look before installing code that runs unsandboxed.</summary>
    public string? Repository { get; set; }

    /// <summary>What the plugin claims to provide, for the browse list. The loaded row reports what
    /// it actually registered — these are the publisher's word, not the host's.</summary>
    public List<string> Capabilities { get; set; } = [];

    public List<PluginCatalogRelease> Releases { get; set; } = [];

    /// <summary>
    /// True when some release targets this host's plugin API, whatever else is wrong with it.
    /// Separates "built for another KHost" from "published without a checksum" — both leave
    /// <see cref="LatestCompatible"/> empty, and telling a host the wrong one sends them looking
    /// for an upgrade that would not help.
    /// </summary>
    public bool HasReleaseForThisHost()
        => Releases.Exists(release => release.ApiVersion == PluginApi.CurrentVersion);

    /// <summary>
    /// The newest release this host can actually load, or null when every release targets a
    /// different plugin API. The loader compares API versions for equality, not a minimum, so a
    /// mismatch here would install cleanly and then sit as Incompatible after the restart.
    /// </summary>
    public PluginCatalogRelease? LatestCompatible()
        => Releases
            .Where(release => release.ApiVersion == PluginApi.CurrentVersion && release.IsInstallable)
            .OrderByDescending(release => PluginVersion.Parse(release.Version))
            .FirstOrDefault();
}

public sealed class PluginCatalogRelease
{
    public string Version { get; set; } = string.Empty;

    public int ApiVersion { get; set; }

    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Hex SHA-256 of the zip. The catalog is the trust root — a release asset swapped after
    /// publication cannot change the payload without an edit here — so a release without one is
    /// never offered for install.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;

    public long? SizeBytes { get; set; }

    public bool IsInstallable
        => !string.IsNullOrWhiteSpace(Sha256)
        && Uri.TryCreate(Url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}
