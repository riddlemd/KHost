namespace KHost.Plugins.Sdk.Models.QueueRotation;

public sealed record QueueRotationContext
{
    /// <summary>Current queue in on-screen order (index 0 sings next).</summary>
    public IReadOnlyList<RotationSinger> Queue { get; init; } = [];

    /// <summary>Set when rotating because this singer just finished performing.</summary>
    public Guid? FinishedSingerId { get; init; }

    /// <summary>Set when rotating because this singer just joined the queue.</summary>
    public Guid? JoiningSingerId { get; init; }

    public QueueRotationConfig Config { get; init; } = new();
    public IReadOnlyDictionary<Guid, int> SongsSungTonight { get; init; } = new Dictionary<Guid, int>();
    public IReadOnlyDictionary<Guid, int> MissedCalls { get; init; } = new Dictionary<Guid, int>();
    public DateTime Now { get; init; } = DateTime.UtcNow;
}
