using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services.QueueRotation;

namespace KHost.Abstractions.Services.QueueRotation;

/// <summary>The host's catalogue of rotation modes, built-in and plugin-supplied.</summary>
/// <remarks>Host-only: the singer queue and the venue settings page use it. A plugin has no
/// business implementing or calling it; to add a mode, implement <see cref="IQueueRotationMode"/>.</remarks>
public interface IQueueRotationStrategyFactory
{
    /// <summary>The strategy a venue's rotation settings call for, with the venue's adjustments
    /// (first-time boost, VIP tier, cool-down) applied around the chosen mode.</summary>
    /// <remarks>Never null: an id no loaded mode answers to resolves to <c>fifo</c>.</remarks>
    IQueueRotationStrategy Resolve(QueueRotationConfig config);

    /// <summary>Every mode a venue can choose, one per id.</summary>
    IReadOnlyList<IQueueRotationMode> GetAllModes();
}
