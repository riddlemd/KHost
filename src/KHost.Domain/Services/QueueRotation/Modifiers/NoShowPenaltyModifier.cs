using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modifiers;

public class NoShowPenaltyModifier : IQueueRotationStrategy
{
    private readonly IQueueRotationStrategy _inner;

    public NoShowPenaltyModifier(IQueueRotationStrategy inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var inner = await _inner.ApplyAsync(context);

        if (context.Config.NoShowDemoteSlots <= 0)
            return inner;

        var demote = context.Config.NoShowDemoteSlots;
        var maxMisses = Math.Max(1, context.Config.NoShowMaxMisses);

        var penalized = inner
            .Where(id => context.MissedCalls.TryGetValue(id, out var m) && m > 0 && m < maxMisses)
            .ToHashSet();

        if (penalized.Count == 0)
            return inner;

        var result = inner.ToList();

        foreach (var id in penalized)
        {
            var current = result.IndexOf(id);
            if (current < 0) continue;

            var target = Math.Min(result.Count - 1, current + demote);
            if (target == current) continue;

            result.RemoveAt(current);
            result.Insert(target, id);
        }

        return result;
    }
}
