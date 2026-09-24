namespace KHost.Abstractions.Models.QueueRotation;

/// <summary>Everything a rotation mode or modifier needs to reorder the queue for one pass.</summary>
public sealed record QueueRotationContext
{
    /// <summary>Current queue in on-screen order (index 0 sings next).</summary>
    public IReadOnlyList<RotationSinger> Queue { get; init; } = [];

    /// <summary>Set when rotating because this singer just finished performing.</summary>
    public Guid? FinishedSingerId { get; init; }

    /// <summary>Set when rotating because this singer just joined the queue.</summary>
    public Guid? JoiningSingerId { get; init; }

    /// <summary>The venue's rotation rules for this pass.</summary>
    public QueueRotationConfig Config { get; init; } = new();

    /// <summary>How many songs each singer has already sung tonight, keyed by singer id. A singer
    /// missing from this map has sung none.</summary>
    public IReadOnlyDictionary<Guid, int> SongsSungTonight { get; init; } = new Dictionary<Guid, int>();

    /// <summary>The moment this pass is being computed, in UTC.</summary>
    public DateTime Now { get; init; } = DateTime.UtcNow;
}
