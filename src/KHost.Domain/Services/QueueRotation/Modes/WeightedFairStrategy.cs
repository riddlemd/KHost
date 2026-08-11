using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modes;

public class WeightedFairStrategy : IQueueRotationMode
{
    public string Id => "weighted-fair";
    public string Name => "Weighted fair";
    public string Description => "Scores singers by a configurable blend of wait time and song count. Highest score goes next.";

    public Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var waitWeight = context.Config.WeightedFairWaitWeight;
        var songWeight = context.Config.WeightedFairSongCountWeight;

        var ordered = context.Queue
            .OrderByDescending(u => Score(u, context, waitWeight, songWeight))
            .Select(u => u.Id)
            .ToList();

        return Task.FromResult<IReadOnlyList<Guid>>(ordered);
    }

    private static double Score(RotationSinger user, QueueRotationContext context, double waitWeight, double songWeight)
    {
        var lastSang = user.LastSangOn ?? DateTime.MinValue;
        var waitMinutes = (context.Now - lastSang).TotalMinutes;
        var songCount = context.SongsSungTonight.TryGetValue(user.Id, out var c) ? c : 0;

        return (waitMinutes * waitWeight) / ((songCount * songWeight) + 1.0);
    }
}
