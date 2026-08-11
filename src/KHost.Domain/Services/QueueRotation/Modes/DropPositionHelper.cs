using KHost.Plugins.Sdk.Models.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modes;

internal static class DropPositionHelper
{
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
