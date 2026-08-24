namespace KHost.Abstractions.Models;

/// <summary>
/// What a pool remembers between picks. Held by the service rather than the row: it is worth
/// nothing after a restart, and writing it would put a database write on every track change.
/// </summary>
public sealed class PoolSelectionState
{
    /// <summary>Where <see cref="PoolSelectionMode.Sequential"/> is up to, per pool.</summary>
    public Dictionary<Guid, int> Cursors { get; } = [];

    /// <summary>Media picked recently, oldest first.</summary>
    public List<Guid> Recent { get; } = [];

    /// <summary>Keeps <see cref="Recent"/> from growing all night in a long-running venue.</summary>
    public void Remember(Guid mediaId, int keep)
    {
        Recent.Add(mediaId);

        var limit = Math.Max(keep, 1);
        if (Recent.Count > limit)
            Recent.RemoveRange(0, Recent.Count - limit);
    }
}
