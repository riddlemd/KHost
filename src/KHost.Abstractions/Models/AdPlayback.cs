namespace KHost.Abstractions.Models;

/// <summary>
/// One ad as it reaches the room: something to look at, something to hear, or both. Composed
/// rather than a single file so a still can carry a voiceover, and so an audio spot can play over
/// whatever is already on screen.
/// </summary>
public sealed class AdPlayback
{
    /// <summary>Video or still. Null leaves whatever is on screen — usually the venue card.</summary>
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
