using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modes;

public class LongestWaitFirstStrategy : IQueueRotationMode
{
    public string Id => "longest-wait-first";
    public string Name => "Longest-wait-first";
    public string Description => "Whoever has been waiting the longest always goes next.";

    public Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var ordered = context.Queue
            .OrderBy(u => u.LastSangOn ?? DateTime.MinValue)
            .Select(u => u.Id)
            .ToList();

        return Task.FromResult<IReadOnlyList<Guid>>(ordered);
    }
}
