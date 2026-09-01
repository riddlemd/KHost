using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services.QueueRotation;
using KHost.Common.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modes;

public class FifoStrategy : IQueueRotationMode
{
    public string Id => "fifo";
    public string Name => "FIFO (classic)";
    public string Description => "Singers rotate in the order they joined. The finished singer drops back to a configurable position.";

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
            context.Config.DropPosition,
            context.Config.DropFixedIndex,
            Random.Shared);

        return Task.FromResult(newOrder);
    }
}
