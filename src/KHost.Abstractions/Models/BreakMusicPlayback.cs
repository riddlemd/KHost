namespace KHost.Abstractions.Models;

/// <summary>
/// What a break music provider is doing, as the provider itself sees it. Narrower than the host's
/// own state: suspending for a singer is the host's business, and a provider never reports it.
/// </summary>
public enum BreakMusicPlayback
{
    Stopped,
    Paused,
    Playing,
}
