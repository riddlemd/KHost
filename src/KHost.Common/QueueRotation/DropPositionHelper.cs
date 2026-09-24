using KHost.Abstractions.Models.QueueRotation;

namespace KHost.Common.QueueRotation;

/// <summary>Where a finished singer's id lands back in the queue, per a rotation strategy's rules.</summary>
public static class DropPositionHelper
{
    /// <summary><paramref name="queue"/> with <paramref name="finishedSingerId"/> moved to where
    /// <paramref name="mode"/> places it, or left out entirely under
    /// <see cref="DropPositionMode.LeavesQueue"/>. <paramref name="fixedIndex"/> only matters for
    /// <see cref="DropPositionMode.FixedIndex"/>; <paramref name="random"/> only for
    /// <see cref="DropPositionMode.RandomBackHalf"/>.</summary>
    public static IReadOnlyList<Guid> ApplyDropPosition(
        IReadOnlyList<Guid> queue,
        Guid finishedSingerId,
        DropPositionMode mode,
        int fixedIndex,
        Random random)
    {
        var list = queue.Where(id => id != finishedSingerId).ToList();

        if (!queue.Contains(finishedSingerId) || mode == DropPositionMode.LeavesQueue)
            return list;

        var insertAt = mode switch
        {
            DropPositionMode.End => list.Count,
            DropPositionMode.FixedIndex => Math.Clamp(fixedIndex, 0, list.Count),
            DropPositionMode.RandomBackHalf => list.Count == 0
                ? 0
                : random.Next(Math.Max(1, list.Count / 2), list.Count + 1),
            _ => list.Count,
        };

        list.Insert(insertAt, finishedSingerId);
        return list;
    }
}
