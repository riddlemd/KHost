using KHost.Abstractions.Models;

namespace KHost.Common.Media;

public static class AdPlaybackExtensions
{
    /// <summary>
    /// Whether the room hears the ad rather than the bed. A still with no audio of its own is
    /// silent, so break music plays on underneath instead of leaving the room quiet.
    /// </summary>
    public static bool HasOwnAudio(this AdPlayback ad) => ad.Audio is not null
        || (ad.Visual is not null && !MediaFormats.IsImage(ad.Visual.Format));
}
