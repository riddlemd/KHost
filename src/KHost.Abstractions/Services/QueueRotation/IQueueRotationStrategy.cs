using KHost.Abstractions.Models.QueueRotation;

namespace KHost.Abstractions.Services.QueueRotation;

public interface IQueueRotationStrategy
{
    /// <summary>Returns the new queue order as singer ids; must include every queued singer exactly once.</summary>
    Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context);
}
