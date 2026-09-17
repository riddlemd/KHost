using KHost.Abstractions.Models;

namespace KHost.Domain.Services.MediaPools;

/// <summary>Save-time guard against a pool that can reach itself, a backstop past the depth cap.</summary>
/// <remarks>A stored cycle is a bug a host cannot fix from the page.</remarks>
public static class MediaPoolCycles
{
    /// <summary>True when a pool can reach itself through its own entries.</summary>
    /// <remarks>Pool is passed separately since its edited entries are not in the map yet.</remarks>
    public static bool CreatesCycle(MediaPool pool, IReadOnlyDictionary<Guid, MediaPool> poolsById)
    {
        var edited = poolsById.ToDictionary(p => p.Key, p => p.Value);
        edited[pool.Id] = pool;

        return Reaches(pool.Id, pool, edited, []);
    }

    private static bool Reaches(Guid target, MediaPool from, IReadOnlyDictionary<Guid, MediaPool> poolsById, HashSet<Guid> seen)
    {
        foreach (var entry in from.Entries)
        {
            if (entry.ChildPoolId is not { } childId)
                continue;

            if (childId == target)
                return true;

            // A pool reached twice down different branches is not a cycle, so this only stops the
            // walk from repeating work. The cycle itself is the childId == target test above.
            if (!seen.Add(childId))
                continue;

            if (poolsById.TryGetValue(childId, out var child) && Reaches(target, child, poolsById, seen))
                return true;
        }

        return false;
    }
}
