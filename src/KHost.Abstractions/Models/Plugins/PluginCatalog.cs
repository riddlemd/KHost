using System.Text.Json.Serialization;

namespace KHost.Abstractions.Models.Plugins;

/// <summary>Installable plugins; fields default, not required, so a bad entry degrades alone.</summary>
public sealed class PluginCatalog
{
    /// <summary>Unrecognised catalogs reject whole; see <see cref="SupportedSchemaVersion"/>.</summary>
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

    /// <summary>Where the source lives, so a host can look before installing unsandboxed code.</summary>
    public string? Repository { get; set; }

    /// <summary>What the plugin claims to provide, for the browse list. The loaded row reports what
    /// it actually registered; these are the publisher's word, not the host's.</summary>
    public List<string> Capabilities { get; set; } = [];

    public List<PluginCatalogRelease> Releases { get; set; } = [];
}

public sealed class PluginCatalogRelease
{
    public string Version { get; set; } = string.Empty;

    public int ApiVersion { get; set; }

    public string Url { get; set; } = string.Empty;

    /// <summary>Hex SHA-256 of the zip; the catalog is the trust root for a swapped release asset.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public long? SizeBytes { get; set; }

    /// <summary>Platform this build targets, e.g. "win-x64"; blank means it runs anywhere.</summary>
    public string? Rid { get; set; }

    // Derived, so it must never be written: the catalog is a published document, and a persisted
    // isInstallable would read like a field a publisher could set, which it is not.
    [JsonIgnore]
    public bool IsInstallable
        => !string.IsNullOrWhiteSpace(Sha256)
        && Uri.TryCreate(Url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}
