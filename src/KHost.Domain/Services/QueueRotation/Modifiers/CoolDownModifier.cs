using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modifiers;

public class CoolDownModifier : IQueueRotationStrategy
{
    private readonly IQueueRotationStrategy _inner;

    public CoolDownModifier(IQueueRotationStrategy inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var inner = await _inner.ApplyAsync(context);

        if (context.Config.CoolDownSlots <= 0)
            return inner;

        if (context.FinishedSingerId is not { } finishedId)
            return inner;

        var result = inner.ToList();
        var current = result.IndexOf(finishedId);
        if (current < 0)
            return inner;

        var minPosition = Math.Min(context.Config.CoolDownSlots, result.Count - 1);
        if (current >= minPosition)
            return inner;

        result.RemoveAt(current);
        var insertAt = Math.Min(minPosition, result.Count);
        result.Insert(insertAt, finishedId);

        return result;
    }
}
