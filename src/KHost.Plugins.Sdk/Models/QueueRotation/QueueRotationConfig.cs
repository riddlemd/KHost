namespace KHost.Plugins.Sdk.Models.QueueRotation;

public class QueueRotationConfig
{
    // Rotation always runs; the default fifo + drop-to-end matches classic karaoke rotation.
    public string StrategyId { get; set; } = "fifo";

    public DropPositionMode DropPosition { get; set; } = DropPositionMode.End;
    public int DropFixedIndex { get; set; } = 0;

    public int TimeBoxMinutes { get; set; } = 0;

    public Guid? VipGroupId { get; set; }

    public bool FirstTimeBoostEnabled { get; set; } = false;
    public int FirstTimeBoostSlots { get; set; } = 1;

    public int NoShowDemoteSlots { get; set; } = 0;
    public int NoShowMaxMisses { get; set; } = 3;

    public int TipBumpWindowMinutes { get; set; } = 0;

    public int CoolDownSlots { get; set; } = 0;

    public double WeightedFairWaitWeight { get; set; } = 1.0;
    public double WeightedFairSongCountWeight { get; set; } = 1.0;

    /// <summary>Every member is a value type or immutable, so a memberwise copy is a full copy.</summary>
    public QueueRotationConfig Clone() => (QueueRotationConfig)MemberwiseClone();
}
