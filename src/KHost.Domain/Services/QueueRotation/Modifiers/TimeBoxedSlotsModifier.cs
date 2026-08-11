using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modifiers;

public class TimeBoxedSlotsModifier : IQueueRotationStrategy
{
    private const double EstimatedMinutesPerSong = 4.0;

    private readonly IQueueRotationStrategy _inner;

    public TimeBoxedSlotsModifier(IQueueRotationStrategy inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var inner = await _inner.ApplyAsync(context);

        if (context.Config.TimeBoxMinutes <= 0)
            return inner;

        var budget = context.Config.TimeBoxMinutes;

        var overBudgetIds = inner
            .Where(id =>
            {
                if (!context.SongsSungTonight.TryGetValue(id, out var songs)) return false;
                return songs * EstimatedMinutesPerSong >= budget;
            })
            .ToHashSet();

        if (overBudgetIds.Count == 0)
            return inner;

        var withinBudget = inner.Where(id => !overBudgetIds.Contains(id)).ToList();
        var overBudget = inner.Where(overBudgetIds.Contains).ToList();

        return [.. withinBudget, .. overBudget];
    }
}
