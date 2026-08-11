using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modifiers;

public class PresenceGatedModifier : IQueueRotationStrategy
{
    private readonly IQueueRotationStrategy _inner;

    public PresenceGatedModifier(IQueueRotationStrategy inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var inner = await _inner.ApplyAsync(context);

        if (!context.Config.PresenceRequired)
            return inner;

        var window = Math.Max(1, context.Config.PresenceWindowMinutes);
        var cutoff = context.Now.AddMinutes(-window);

        var presentIds = context.Queue
            .Where(u => u.LastCheckinOn is { } checkin && checkin >= cutoff)
            .Select(u => u.Id)
            .ToHashSet();

        var present = inner.Where(presentIds.Contains).ToList();
        var absent = inner.Where(id => !presentIds.Contains(id)).ToList();

        return [.. present, .. absent];
    }
}
