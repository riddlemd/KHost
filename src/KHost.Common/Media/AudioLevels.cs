using KHost.Abstractions.Models;

namespace KHost.Common.Media;

public static class AudioLevels
{
    /// <summary>
    /// A stored volume is whatever was last written — a hand-edited settings file or an older
    /// build's range included — so every read passes through here before it reaches a mixer.
    /// </summary>
    public static int ClampVolume(int volume) => Math.Clamp(volume, AudioMix.MinVolume, AudioMix.MaxVolume);
}
