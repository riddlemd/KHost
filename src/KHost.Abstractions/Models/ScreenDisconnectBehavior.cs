namespace KHost.Abstractions.Models;

/// <summary>What to do with the current performance when the last screen disconnects.</summary>
public enum ScreenDisconnectBehavior
{
    /// <summary>Pause, and pick up where it left off once a screen reconnects.</summary>
    ResumeOnReconnect,

    /// <summary>Pause and rewind to the start, leaving the host to press play.</summary>
    RestartFromStart,

    /// <summary>End the performance outright.</summary>
    CancelPerformance,
}
