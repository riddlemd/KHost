using KHost.Abstractions.Models.Plugins;
namespace KHost.Abstractions.Models.Plugins;

/// <summary>A catalog read plus when it was read, cached so the browse list renders offline.</summary>
public sealed record PluginCatalogSnapshot
{
    public const string CacheKey = "PluginCatalog";

    public required PluginCatalog Catalog { get; init; }

    public required DateTime FetchedUtc { get; init; }

    /// <summary>Replayed as If-None-Match so an unchanged catalog costs only a 304.</summary>
    public string? ETag { get; init; }
}
