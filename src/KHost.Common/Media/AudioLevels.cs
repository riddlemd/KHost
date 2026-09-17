using KHost.Abstractions.Models;

namespace KHost.Common.Media;

public static class AudioLevels
{
    /// <summary>A stored volume is whatever was last written, older build's range included.</summary>
    /// <remarks>Every read passes through here before reaching a mixer.</remarks>
    public static int ClampVolume(int volume) => Math.Clamp(volume, AudioMix.MinVolume, AudioMix.MaxVolume);
}
