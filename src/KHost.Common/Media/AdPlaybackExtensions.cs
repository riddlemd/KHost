using KHost.Abstractions.Models;

namespace KHost.Common.Media;

/// <summary>Whether an <see cref="AdPlayback"/> carries its own audio or needs break music underneath it.</summary>
public static class AdPlaybackExtensions
{
    /// <summary>Whether the room hears the ad rather than the bed underneath it.</summary>
    /// <remarks>A still with no audio of its own is silent, so break music plays on instead.</remarks>
    public static bool HasOwnAudio(this AdPlayback ad) => ad.Audio is not null
        || (ad.Visual is not null && !MediaFormats.IsImage(ad.Visual.Format));
}
