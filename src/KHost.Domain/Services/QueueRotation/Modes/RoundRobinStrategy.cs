using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modes;

public class RoundRobinStrategy : IQueueRotationMode
{
    public string Id => "round-robin";
    public string Name => "Round robin (fixed order)";
    public string Description => "Each singer gets exactly one slot per round. No one goes twice until everyone has gone once.";

    public Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var queueIds = context.Queue.Select(u => u.Id).ToList();

        if (context.JoiningSingerId is { } && context.FinishedSingerId is null)
            return Task.FromResult<IReadOnlyList<Guid>>(queueIds);

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
