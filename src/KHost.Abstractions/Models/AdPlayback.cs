namespace KHost.Abstractions.Models;

/// <summary>One ad as it reaches the room: a visual, an audio spot, or both, composed together.</summary>
public sealed class AdPlayback
{
    /// <summary>Video or still. Null leaves whatever is on screen, usually the venue card.</summary>
    public Media? Visual { get; init; }

    /// <summary>Audio of its own. Null means the visual's own track, if it has one.</summary>
    public Media? Audio { get; init; }

    /// <summary>Where the audio starts, so a clip is trimmed without re-encoding it.</summary>
    public TimeSpan AudioStart { get; init; }

    /// <summary>The host clock ends the ad on this, whatever the underlying files are.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Nothing to look at and nothing to hear is not an ad.</summary>
    public bool IsEmpty => Visual is null && Audio is null;
}
