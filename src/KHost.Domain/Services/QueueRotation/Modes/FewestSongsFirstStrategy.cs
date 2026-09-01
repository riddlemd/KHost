using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation.Modes;

public class FewestSongsFirstStrategy : IQueueRotationMode
{
    public string Id => "fewest-songs-first";
    public string Name => "Fewest-songs-first";
    public string Description => "Whoever has sung the fewest songs tonight goes next.";

    public Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context)
    {
        var ordered = context.Queue
            .OrderBy(u => context.SongsSungTonight.TryGetValue(u.Id, out var count) ? count : 0)
            .ThenBy(u => u.LastSangOn ?? DateTime.MinValue)
            .Select(u => u.Id)
            .ToList();

        return Task.FromResult<IReadOnlyList<Guid>>(ordered);
    }
}
