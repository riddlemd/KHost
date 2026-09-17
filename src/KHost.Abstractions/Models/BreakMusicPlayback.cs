namespace KHost.Abstractions.Models;

/// <summary>What a provider reports doing: narrower than the host's state; never "suspended".</summary>
public enum BreakMusicPlayback
{
    Stopped,
    Paused,
    Playing,
}
