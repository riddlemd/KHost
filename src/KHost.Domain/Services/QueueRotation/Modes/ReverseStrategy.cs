using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modes;

public class ReverseStrategy : IQueueRotationMode
{
    public string Id => "reverse";
    public string Name => "Reverse / LIFO";
    public string Description => "Newest singer performs next — the queue runs in reverse join order.";

    public Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var queueIds = context.Queue.Select(u => u.Id).ToList();

        if (context.JoiningSingerId is { } joinerId && context.FinishedSingerId is null)
        {
            queueIds.Remove(joinerId);
            queueIds.Insert(0, joinerId);
            return Task.FromResult<IReadOnlyList<Guid>>(queueIds);
        }

        if (context.FinishedSingerId is not { } finishedId)
            return Task.FromResult<IReadOnlyList<Guid>>(queueIds);

        var newOrder = DropPositionHelper.ApplyDropPosition(
            queueIds,
            finishedId,
            DropPositionMode.End,
            fixedIndex: 0,
            Random.Shared);

        return Task.FromResult(newOrder);
    }
}
