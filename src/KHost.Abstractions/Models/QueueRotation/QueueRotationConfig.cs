namespace KHost.Abstractions.Models.QueueRotation;

/// <summary>A venue's queue rotation rules. Rotation always runs; these settings only change how.</summary>
public class QueueRotationConfig
{
    // Rotation always runs; the default fifo + drop-to-end matches classic karaoke rotation.
    /// <summary>The rotation mode's own id, e.g. <c>"fifo"</c> or <c>"weighted-fair"</c>. An
    /// unrecognised id falls back to the default (fifo, drop-to-end) rotation.</summary>
    public string StrategyId { get; set; } = "fifo";

    /// <summary>Where a finished singer rejoins the queue.</summary>
    public DropPositionMode DropPosition { get; set; } = DropPositionMode.End;

    /// <summary>The slot a finished singer rejoins at, counted from the front. Only read when
    /// <see cref="DropPosition"/> is <see cref="DropPositionMode.FixedIndex"/>.</summary>
    public int DropFixedIndex { get; set; } = 0;

    /// <summary>Members of this group always sing ahead of everyone else, in whatever order the
    /// rotation mode would otherwise give them. Null means no group gets priority.</summary>
    public Guid? VipGroupId { get; set; }

    /// <summary>Moves a singer's first song of the night ahead in the queue. Off by default.</summary>
    public bool FirstTimeBoostEnabled { get; set; } = false;

    /// <summary>How many slots a first-timer's turn is moved forward, when
    /// <see cref="FirstTimeBoostEnabled"/> is on.</summary>
    public int FirstTimeBoostSlots { get; set; } = 1;

    /// <summary>How many of the front slots a singer who just finished cannot immediately rejoin —
    /// they wait behind that many other singers first. Zero means no restriction.</summary>
    public int CoolDownSlots { get; set; } = 0;

    /// <summary>How heavily time waited counts under the weighted-fair mode, relative to
    /// <see cref="WeightedFairSongCountWeight"/>. Only read by that mode.</summary>
    public double WeightedFairWaitWeight { get; set; } = 1.0;

    /// <summary>How heavily songs already sung tonight count against a singer under the
    /// weighted-fair mode, relative to <see cref="WeightedFairWaitWeight"/>. Only read by that
    /// mode.</summary>
    public double WeightedFairSongCountWeight { get; set; } = 1.0;

    /// <summary>An independent copy; changing it never affects the original.</summary>
    public QueueRotationConfig Clone() => (QueueRotationConfig)MemberwiseClone();
}
