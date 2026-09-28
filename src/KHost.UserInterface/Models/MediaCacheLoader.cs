using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.UserInterface.Models;

/// <summary>Reads a set of media rows into a lookup keyed by id, so both queue panels look a
/// performance's song up by <c>TryGetValue</c> rather than a linear scan per row.</summary>
public static class MediaCacheLoader
{
    /// <summary>Every id is read once, in parallel, regardless of how many rows share it.</summary>
    public static async Task<Dictionary<Guid, Media?>> ReadByIdAsync(IMediaService mediaService, IEnumerable<Guid> ids)
    {
        var distinctIds = ids.Distinct().ToList();
        var media = await Task.WhenAll(distinctIds.Select(id => mediaService.ReadAsync(id)));

        return distinctIds.Zip(media).ToDictionary(x => x.First, x => x.Second);
    }
}
