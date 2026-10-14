namespace KHost.Abstractions.Models.QueueRotation;

/// <summary>Where a finished singer rejoins the queue.</summary>
public enum DropPositionMode
{
    /// <summary>Rejoins at the back of the queue.</summary>
    End = 0,

    /// <summary>Rejoins at a fixed slot from the front, given by
    /// <see cref="QueueRotationConfig.DropFixedIndex"/> (clamped to the queue's length).</summary>
    FixedIndex = 1,

    /// <summary>Rejoins at a random slot somewhere in the back half of the queue.</summary>
    RandomBackHalf = 2,

    /// <summary>The finished singer is removed from the queue instead of re-queued.</summary>
    LeavesQueue = 3,
}
