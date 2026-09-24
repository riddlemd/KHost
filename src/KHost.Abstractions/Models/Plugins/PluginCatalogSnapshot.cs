using KHost.Abstractions.Models.Plugins;
namespace KHost.Abstractions.Models.Plugins;

/// <summary>A catalog read plus when it was read, cached so the browse list renders offline.</summary>
public sealed record PluginCatalogSnapshot
{
    /// <summary>The cache key this snapshot is stored under.</summary>
    public const string CacheKey = "PluginCatalog";

    /// <summary>The catalog as it last read successfully.</summary>
    public required PluginCatalog Catalog { get; init; }

    /// <summary>When this snapshot was fetched, in UTC.</summary>
    public required DateTime FetchedUtc { get; init; }

    /// <summary>An opaque marker from the last successful fetch, or null if none was given. Lets
    /// the next fetch confirm the catalog is unchanged without re-downloading it.</summary>
    public string? ETag { get; init; }
}
