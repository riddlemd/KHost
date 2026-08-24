using KHost.Abstractions.Models;

namespace KHost.Domain.Services.MediaPools;

/// <summary>
/// Picks the next media out of a pool tree. Pure and seedable on purpose: the randomised modes
/// are only testable if the caller owns the <see cref="Random"/>.
/// </summary>
public static class MediaPoolSelector
{
    /// <summary>
    /// Backstop for a pool graph that references itself. Saving rejects a cycle, so reaching this
    /// means the rows were edited outside the app — it must not hang the console mid-show.
    /// </summary>
    private const int MaxDepth = 16;

    /// <summary>
    /// Null when the tree holds nothing playable. Tried twice: once honouring the no-repeat
    /// window, then once ignoring it, so a two-track pool still plays rather than falling silent.
    /// </summary>
    public static MediaPoolEntry? SelectNext(
        MediaPool root,
        IReadOnlyDictionary<Guid, MediaPool> poolsById,
        PoolSelectionState state,
        Random random)
    {
        var picked = Select(root, poolsById, state, random, enforceNoRepeat: true, [], 0)
            ?? Select(root, poolsById, state, random, enforceNoRepeat: false, [], 0);

        if (picked is not null)
            state.Remember(IdentityOf(picked), root.NoRepeatCount);

        return picked;
    }

    private static MediaPoolEntry? Select(
        MediaPool pool,
        IReadOnlyDictionary<Guid, MediaPool> poolsById,
        PoolSelectionState state,
        Random random,
        bool enforceNoRepeat,
        HashSet<Guid> visited,
        int depth)
    {
        if (depth >= MaxDepth || !visited.Add(pool.Id))
            return null;

        try
        {
            var candidates = pool.Entries
                .Where(e => e.ChildPoolId is { } childId
                    ? poolsById.ContainsKey(childId)
                    : e.MediaId is not null || e.AudioMediaId is not null)
                .OrderBy(e => e.Position)
                .ToList();

            // Entries are dropped as they are ruled out, so a pool whose first pick is blocked
            // keeps trying its siblings rather than reporting the whole subtree as empty.
            while (candidates.Count > 0)
            {
                var index = PickIndex(pool, candidates, state, random);
                if (index < 0)
                    return null;

                var entry = candidates[index];

                if (!entry.IsPool)
                {
                    if (!enforceNoRepeat || !IsBlocked(IdentityOf(entry), pool.NoRepeatCount, state))
                        return entry;
                }
                else if (poolsById.TryGetValue(entry.ChildPoolId!.Value, out var child))
                {
                    var nested = Select(child, poolsById, state, random, enforceNoRepeat, visited, depth + 1);
                    if (nested is not null)
                        return nested;
                }

                candidates.RemoveAt(index);
            }

            return null;
        }
        finally
        {
            // Removed on the way out so a pool reachable down two separate branches is not
            // mistaken for a cycle the second time it is reached.
            visited.Remove(pool.Id);
        }
    }

    /// <summary>
    /// What the no-repeat window remembers. The media where there is one, so the same track listed
    /// twice to weight it does not get to play back to back; the entry itself otherwise.
    /// </summary>
    private static Guid IdentityOf(MediaPoolEntry entry) => entry.MediaId ?? entry.AudioMediaId ?? entry.Id;

    private static bool IsBlocked(Guid mediaId, int noRepeatCount, PoolSelectionState state)
    {
        if (noRepeatCount <= 0)
            return false;

        var window = Math.Min(noRepeatCount, state.Recent.Count);

        for (var i = state.Recent.Count - window; i < state.Recent.Count; i++)
        {
            if (state.Recent[i] == mediaId)
                return true;
        }

        return false;
    }

    private static int PickIndex(MediaPool pool, List<MediaPoolEntry> candidates, PoolSelectionState state, Random random)
        => pool.SelectionMode switch
        {
            PoolSelectionMode.Sequential => NextSequential(pool, candidates, state),
            PoolSelectionMode.Weighted => NextWeighted(candidates, random),
            _ => random.Next(candidates.Count),
        };

    private static int NextSequential(MediaPool pool, List<MediaPoolEntry> candidates, PoolSelectionState state)
    {
        // The cursor counts picks rather than indexing the list, because entries drop out of
        // candidates as they are ruled out and an index into a shrinking list would skip around.
        var cursor = state.Cursors.TryGetValue(pool.Id, out var stored) ? stored : 0;

        state.Cursors[pool.Id] = cursor + 1;

        return cursor % candidates.Count;
    }

    private static int NextWeighted(List<MediaPoolEntry> candidates, Random random)
    {
        var total = candidates.Sum(e => Math.Max(e.Weight, 0));

        // Every weight at zero is the host excluding the lot, not an invitation to pick evenly.
        if (total <= 0)
            return -1;

        var roll = random.Next(total);

        for (var i = 0; i < candidates.Count; i++)
        {
            roll -= Math.Max(candidates[i].Weight, 0);

            if (roll < 0)
                return i;
        }

        return candidates.Count - 1;
    }
}
