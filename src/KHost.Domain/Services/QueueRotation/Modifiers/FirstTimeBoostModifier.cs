using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modifiers;

public class FirstTimeBoostModifier : IQueueRotationStrategy
{
    private readonly IQueueRotationStrategy _inner;

    public FirstTimeBoostModifier(IQueueRotationStrategy inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var inner = await _inner.ApplyAsync(context);

        if (!context.Config.FirstTimeBoostEnabled)
            return inner;

        var startOfToday = context.Now.Date;

        var firstTimerIds = context.Queue
            .Where(u => u.Id != context.FinishedSingerId
                && (u.LastSangOn is null || u.LastSangOn < startOfToday)
                && (!context.SongsSungTonight.TryGetValue(u.Id, out var c) || c == 0))
            .Select(u => u.Id)
            .ToHashSet();

        if (firstTimerIds.Count == 0)
            return inner;

        var boostSlots = Math.Max(1, context.Config.FirstTimeBoostSlots);
        var result = inner.ToList();

        foreach (var id in firstTimerIds)
        {
            var current = result.IndexOf(id);
            if (current < 0) continue;

            var target = Math.Max(0, current - boostSlots);
            if (target == current) continue;

            result.RemoveAt(current);
            result.Insert(target, id);
        }

        return result;
    }
}
