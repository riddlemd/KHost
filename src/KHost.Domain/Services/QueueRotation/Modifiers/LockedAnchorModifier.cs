using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modifiers;

public class LockedAnchorModifier : IQueueRotationStrategy
{
    private readonly IQueueRotationStrategy _inner;

    public LockedAnchorModifier(IQueueRotationStrategy inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var inner = await _inner.ApplyAsync(context);

        if (context.Config.AnchorSingerId is not { } anchorId)
            return inner;

        if (context.Config.AnchorEveryN <= 0)
            return inner;

        if (!context.Queue.Any(u => u.Id == anchorId))
            return inner;

        var n = context.Config.AnchorEveryN;
        var others = inner.Where(id => id != anchorId).ToList();
        var result = new List<Guid>(others.Count + 1);

        var slot = 0;
        var nextAnchorAt = 0;

        foreach (var id in others)
        {
            if (slot == nextAnchorAt)
            {
                result.Add(anchorId);
                nextAnchorAt += n;
                slot++;
            }
            result.Add(id);
            slot++;
        }

        if (!result.Contains(anchorId))
            result.Add(anchorId);

        return result;
    }
}
