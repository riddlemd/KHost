using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modifiers;

public class VipTierModifier : IQueueRotationStrategy
{
    private readonly IQueueRotationStrategy _inner;

    public VipTierModifier(IQueueRotationStrategy inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var inner = await _inner.ApplyAsync(context);

        if (context.Config.VipGroupId is not { } vipGroupId)
            return inner;

        var vipIds = context.Queue
            .Where(u => u.GroupIds.Contains(vipGroupId))
            .Select(u => u.Id)
            .ToHashSet();

        if (vipIds.Count == 0)
            return inner;

        var vips = inner.Where(vipIds.Contains).ToList();
        var others = inner.Where(id => !vipIds.Contains(id)).ToList();

        return [.. vips, .. others];
    }
}
