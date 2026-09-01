using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modes;

public class ShuffleBucketStrategy : IQueueRotationMode
{
    public string Id => "shuffle-bucket";
    public string Name => "Shuffle bucket";
    public string Description => "The queue is shuffled into a random order each round. Everyone sings once before the next shuffle.";

    public Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var ordered = context.Queue
            .Select(u => new
            {
                u.Id,
                Songs = context.SongsSungTonight.TryGetValue(u.Id, out var c) ? c : 0,
                Tiebreak = Random.Shared.Next(),
            })
            .OrderBy(x => x.Songs)
            .ThenBy(x => x.Tiebreak)
            .Select(x => x.Id)
            .ToList();

        return Task.FromResult<IReadOnlyList<Guid>>(ordered);
    }
}
