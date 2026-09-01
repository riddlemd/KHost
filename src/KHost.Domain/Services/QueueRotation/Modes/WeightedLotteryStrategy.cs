using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modes;

public class WeightedLotteryStrategy : IQueueRotationMode
{
    public string Id => "weighted-lottery";
    public string Name => "Weighted lottery";
    public string Description => "Random draw where singers who've waited longer get more tickets, improving their odds.";

    public Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var pool = context.Queue
            .Where(u => u.Id != context.FinishedSingerId)
            .ToList();

        var ordered = new List<Guid>(pool.Count);
        var weights = pool.ToDictionary(u => u.Id, u => Weight(u, context));

        while (pool.Count > 0)
        {
            var picked = WeightedPick(pool, weights);
            ordered.Add(picked.Id);
            pool.Remove(picked);
        }

        if (context.FinishedSingerId is { } finishedId && context.Queue.Any(u => u.Id == finishedId))
            ordered.Add(finishedId);

        return Task.FromResult<IReadOnlyList<Guid>>(ordered);
    }

    private static double Weight(RotationSinger user, QueueRotationContext context)
    {
        var lastSang = user.LastSangOn ?? DateTime.MinValue;
        var waitMinutes = (context.Now - lastSang).TotalMinutes;
        var songCount = context.SongsSungTonight.TryGetValue(user.Id, out var c) ? c : 0;

        return Math.Max(0.001, waitMinutes) / (songCount + 1.0);
    }

    private static RotationSinger WeightedPick(List<RotationSinger> pool, Dictionary<Guid, double> weights)
    {
        var total = pool.Sum(u => weights[u.Id]);
        var roll = Random.Shared.NextDouble() * total;
        var running = 0.0;

        foreach (var user in pool)
        {
            running += weights[user.Id];
            if (roll <= running) return user;
        }

        return pool[^1];
    }
}
