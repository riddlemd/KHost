using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modifiers;

public class TipBumpModifier : IQueueRotationStrategy
{
    private readonly IQueueRotationStrategy _inner;

    public TipBumpModifier(IQueueRotationStrategy inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var inner = await _inner.ApplyAsync(context);

        if (context.Config.TipBumpWindowMinutes <= 0)
            return inner;

        var cutoff = context.Now.AddMinutes(-context.Config.TipBumpWindowMinutes);

        var recentTipperIds = context.Queue
            .Where(u => u.Id != context.FinishedSingerId
                && u.LastTippedOn is { } tipped
                && tipped >= cutoff)
            .Select(u => u.Id)
            .ToHashSet();

        if (recentTipperIds.Count == 0)
            return inner;

        var result = inner.ToList();

        foreach (var id in recentTipperIds)
        {
            var current = result.IndexOf(id);
            if (current <= 0) continue;

            result.RemoveAt(current);
            result.Insert(current - 1, id);
        }

        return result;
    }
}
