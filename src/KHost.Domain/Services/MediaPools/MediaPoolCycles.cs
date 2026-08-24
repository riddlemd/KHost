using KHost.Abstractions.Models;

namespace KHost.Domain.Services.MediaPools;

/// <summary>
/// Save-time guard against a pool that can reach itself. The selector caps its own depth as a
/// backstop, but a cycle stored in the database is a bug a host cannot see or fix from the page.
/// </summary>
public static class MediaPoolCycles
{
    /// <summary>
    /// True when <paramref name="pool"/> can reach itself through its entries. The pool being
    /// saved is passed in separately because its edited entries are not in the map yet.
    /// </summary>
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
            // walk from repeating work — the cycle itself is the childId == target test above.
            if (!seen.Add(childId))
                continue;

            if (poolsById.TryGetValue(childId, out var child) && Reaches(target, child, poolsById, seen))
                return true;
        }

        return false;
    }
}
