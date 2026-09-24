using KHost.Abstractions.Models.QueueRotation;

namespace KHost.Abstractions.Services.QueueRotation;

/// <summary>Decides the singer rotation's new order when a singer joins or finishes.</summary>
/// <remarks>A plugin does not implement this alone: implement <see cref="IQueueRotationMode"/>,
/// which is what the host discovers and offers a venue. The host may wrap the chosen mode with the
/// venue's own adjustments (first-time boost, VIP tier, cool-down), so the order returned here is
/// not always the order the room sees.</remarks>
public interface IQueueRotationStrategy
{
    /// <summary>New queue order as singer ids, index 0 singing next.</summary>
    /// <remarks>
    /// <para>Called when a singer joins (<see cref="QueueRotationContext.JoiningSingerId"/> set) and
    /// after one finishes (<see cref="QueueRotationContext.FinishedSingerId"/> set). Leaving the
    /// finished singer out takes them off the rotation; that is the only singer a strategy may drop.
    /// Anyone else missing is put back at the end, and duplicates and ids not in the queue are
    /// ignored, so a sloppy answer cannot lose a singer. Honouring
    /// <see cref="QueueRotationConfig.DropPosition"/> for the finished singer is the strategy's job;
    /// <c>KHost.Common</c>'s <c>DropPositionHelper</c> does it.</para>
    /// <para>The rotation is held while this runs: never call back into
    /// <see cref="ISingerQueueService"/> from here, or it deadlocks. Answer from
    /// <paramref name="context"/>, which carries everything the built-in strategies use. A strategy
    /// that throws is logged and the queue keeps its current order.</para>
    /// </remarks>
    Task<IReadOnlyList<Guid>> ApplyAsync(QueueRotationContext context);
}
