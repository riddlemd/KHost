namespace KHost.Domain.Services;

/// <summary>The venue's playback level as a mixer gain. Shared by every provider that pushes the
/// venue's own volume directly — BreakMusicService's external providers and the screen's own
/// channels alike — so a stray value out of range cannot make one of them louder than the other.</summary>
internal static class VenueVolume
{
    internal static float ToGain(int percent) => Math.Clamp(percent / 100f, 0f, 1f);
}
