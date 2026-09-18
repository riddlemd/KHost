namespace KHost.Abstractions.Models;

/// <summary>Whether a queued turn has something to play yet.</summary>
/// <remarks>A property of the turn, not of the library row behind it: the row says what KHost has,
/// this says what one queued performance can start right now. Derived rather than stored, because
/// renders do not outlive the process and a column would read Prepared after every restart.
/// </remarks>
public enum PerformancePreparation
{
    /// <summary>Nothing rendered, and nothing rendering. For a file the host can transcode this is
    /// invisible: playing simply costs what it always did.</summary>
    Unprepared,

    /// <summary>Being rendered now. The turn is playable only if its source is playable as it is.
    /// </summary>
    Preparing,

    /// <summary>Rendered. Starting this turn is a stream copy.</summary>
    Prepared,
}
