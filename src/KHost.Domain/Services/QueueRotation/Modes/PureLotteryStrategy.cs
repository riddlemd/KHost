using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modes;

public class PureLotteryStrategy : IQueueRotationMode
{
    public string Id => "pure-lottery";
    public string Name => "Pure lottery";
    public string Description => "A random singer is drawn each turn — every singer has equal odds.";

    public Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var others = context.Queue
            .Select(u => u.Id)
            .Where(id => id != context.FinishedSingerId)
            .ToList();

        Shuffle(others);

        if (context.FinishedSingerId is { } finishedId && context.Queue.Any(u => u.Id == finishedId))
            others.Add(finishedId);

        return Task.FromResult<IReadOnlyList<Guid>>(others);
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
