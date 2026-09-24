using System.Text.Json.Serialization;

namespace KHost.Abstractions.Models.Plugins;

/// <summary>Installable plugins; fields default, not required, so a bad entry degrades alone.</summary>
public sealed class PluginCatalog
{
    /// <summary>Unrecognised catalogs reject whole; see <see cref="SupportedSchemaVersion"/>.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>The schema this document was written against. A value the host does not
    /// recognise (see <see cref="SupportedSchemaVersion"/>) rejects the whole catalog.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Every plugin the catalog currently lists, installable or not.</summary>
    public List<PluginCatalogEntry> Plugins { get; set; } = [];
}

/// <summary>One plugin's listing: its identity plus every release ever published for it.</summary>
public sealed class PluginCatalogEntry
{
    /// <summary>Matches the plugin's own <c>manifest.json</c> id, which is how an installed row and
    /// a catalog row are recognised as the same plugin.</summary>
    public Guid Id { get; set; }

    /// <summary>Shown on the Available tab; not required to match the loaded plugin's own name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Publisher's name, shown on the Available tab. Null when the publisher gave none.</summary>
    public string? Author { get; set; }

    /// <summary>Publisher's description, shown on the Available tab. Null when the publisher gave
    /// none.</summary>
    public string? Description { get; set; }

    /// <summary>Where the source lives, so a host can look before installing unsandboxed code.</summary>
    public string? Repository { get; set; }

    /// <summary>What the plugin claims to provide, for the browse list. The loaded row reports what
    /// it actually registered; these are the publisher's word, not the host's.</summary>
    public List<string> Capabilities { get; set; } = [];

    /// <summary>Every release published for this plugin; a host installs the one matching its own
    /// plugin API version and platform.</summary>
    public List<PluginCatalogRelease> Releases { get; set; } = [];
}

/// <summary>One published build of a plugin, for one plugin API version and (optionally) one
/// platform.</summary>
public sealed class PluginCatalogRelease
{
    /// <summary>The plugin's own version string; shown to the host and otherwise not interpreted.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>The <see cref="PluginApi.CurrentVersion"/> this build was compiled against. A host
    /// installs only a release whose value matches its own exactly.</summary>
    public int ApiVersion { get; set; }

    /// <summary>Where to download the release zip. Must be https for <see cref="IsInstallable"/>
    /// to be true.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Hex SHA-256 of the zip; the catalog is the trust root for a swapped release asset.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Download size in bytes, for the install progress bar. Null when unknown.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>Platform this build targets, e.g. "win-x64"; blank means it runs anywhere.</summary>
    public string? Rid { get; set; }

    // Derived, so it must never be written: the catalog is a published document, and a persisted
    // isInstallable would read like a field a publisher could set, which it is not.
    /// <summary>True when this release can actually be installed: an https download URL and a
    /// checksum are both present. False means the release is unverifiable, not that it is
    /// incompatible — the publisher needs to fix the entry, not the host.</summary>
    [JsonIgnore]
    public bool IsInstallable
        => !string.IsNullOrWhiteSpace(Sha256)
        && Uri.TryCreate(Url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}
