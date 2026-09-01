using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services.QueueRotation;

namespace KHost.UnitTests.Domain.Services.QueueRotation;

internal class IdentityInnerStrategy : IQueueRotationStrategy
{
    public Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context) =>
        Task.FromResult<IReadOnlyList<Guid>>(context.Queue.Select(u => u.Id).ToList());
}

internal static class QueueRotationTestHelpers
{
    // The name is just a debugging label; RotationSinger snapshots carry no display name.
    public static RotationSinger User(string name, DateTime? lastSangOn = null, params Guid[] groupIds)
    {
        _ = name;

        return new RotationSinger
        {
            Id = Guid.NewGuid(),
            LastSangOn = lastSangOn,
            GroupIds = groupIds,
        };
    }

    public static QueueRotationContext Context(
        IReadOnlyList<RotationSinger> queue,
        Guid? finishedSingerId = null,
        Guid? joiningSingerId = null,
        QueueRotationConfig? config = null,
        IReadOnlyDictionary<Guid, int>? songsSung = null,
        DateTime? now = null)
    {
        return new QueueRotationContext
        {
            Queue = queue,
            FinishedSingerId = finishedSingerId,
            JoiningSingerId = joiningSingerId,
            Config = config ?? new QueueRotationConfig(),
            SongsSungTonight = songsSung ?? new Dictionary<Guid, int>(),
            Now = now ?? DateTime.UtcNow,
        };
    }
}
