using KHost.Abstractions.Services.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modifiers;
using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;

namespace KHost.Domain.Services.QueueRotation;

public class QueueRotationStrategyFactory : IQueueRotationStrategyFactory
{
    private readonly IReadOnlyDictionary<string, IQueueRotationMode> _modes;

    public QueueRotationStrategyFactory(IEnumerable<IQueueRotationMode> modes)
    {
        // First-wins: built-ins register before plugins, so a plugin reusing a taken id is
        // ignored rather than hijacking it (or crashing startup on a duplicate key).
        _modes = modes.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
    }

    public IQueueRotationStrategy Resolve(QueueRotationConfig config)
    {
        if (!_modes.TryGetValue(config.StrategyId, out var strategy))
            strategy = _modes["fifo"];

        var result = (IQueueRotationStrategy)strategy;

        if (config.NoShowDemoteSlots > 0)
            result = new NoShowPenaltyModifier(result);
        if (config.TimeBoxMinutes > 0)
            result = new TimeBoxedSlotsModifier(result);
        if (config.TipBumpWindowMinutes > 0)
            result = new TipBumpModifier(result);
        if (config.FirstTimeBoostEnabled)
            result = new FirstTimeBoostModifier(result);
        if (config.AnchorEveryN > 0 && config.AnchorSingerId.HasValue)
            result = new LockedAnchorModifier(result);
        if (config.VipGroupId.HasValue)
            result = new VipTierModifier(result);
        if (config.CoolDownSlots > 0)
            result = new CoolDownModifier(result);
        if (config.PresenceRequired)
            result = new PresenceGatedModifier(result);

        return result;
    }

    public IReadOnlyList<IQueueRotationMode> GetAllModes() => _modes.Values.ToList();
}
