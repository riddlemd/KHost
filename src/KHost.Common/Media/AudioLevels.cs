using KHost.Abstractions.Models;

namespace KHost.Common.Media;

/// <summary>Keeps a stored or incoming volume inside the mixer's supported range.</summary>
public static class AudioLevels
{
    /// <summary>A stored volume is whatever was last written, older build's range included.</summary>
    /// <remarks>Every read passes through here before reaching a mixer.</remarks>
    public static int ClampVolume(int volume) => Math.Clamp(volume, AudioMix.MinVolume, AudioMix.MaxVolume);
}
