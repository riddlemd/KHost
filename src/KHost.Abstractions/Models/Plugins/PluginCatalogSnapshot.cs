using KHost.Common.Plugins;
namespace KHost.Abstractions.Models.Plugins;

/// <summary>
/// A catalog read paired with when it was read, persisted through <c>ICacheService</c> under
/// <see cref="CacheKey"/> so the browse list renders in a room with no working wifi.
/// </summary>
public sealed record PluginCatalogSnapshot
{
    public const string CacheKey = "PluginCatalog";

    public required PluginCatalog Catalog { get; init; }

    public required DateTime FetchedUtc { get; init; }

    /// <summary>Whatever the server sent, replayed as If-None-Match so an unchanged catalog costs a 304.</summary>
    public string? ETag { get; init; }
}
