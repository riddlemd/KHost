namespace KHost.Plugins.Sdk.Models.QueueRotation;

public enum DropPositionMode
{
    End,
    FixedIndex,
    RandomBackHalf,
    /// <summary>The finished singer is removed from the queue instead of re-queued.</summary>
    LeavesQueue,
}
