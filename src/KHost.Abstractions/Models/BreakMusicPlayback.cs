namespace KHost.Abstractions.Models;

/// <summary>What a provider reports doing: narrower than the host's state; never "suspended".</summary>
public enum BreakMusicPlayback
{
    /// <summary>Nothing is playing.</summary>
    Stopped = 0,

    /// <summary>Playback is paused, part-way through a track.</summary>
    Paused = 1,

    /// <summary>A track is actively playing.</summary>
    Playing = 2,
}
