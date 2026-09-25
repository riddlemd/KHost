using KHost.Abstractions.Models;

namespace KHost.Common.Media;

/// <summary>Keeps a stored or incoming volume inside the mixer's supported range.</summary>
public static class AudioLevels
{
    /// <summary>A stored volume is whatever was last written, older build's range included.</summary>
    /// <remarks>Every read passes through here before reaching a mixer.</remarks>
    public static int ClampVolume(int volume) => Math.Clamp(volume, AudioMix.MinVolume, AudioMix.MaxVolume);

    /// <summary>The level, clamped, that a track rides at under this mix: full for the music, the
    /// singer's own level for a lead whose voice has one, otherwise the level for its role.</summary>
    public static int VolumeFor(this AudioMix mix, AudioTrackRole role, string? voice = null) => role switch
    {
        AudioTrackRole.Music => AudioMix.MaxVolume,
        AudioTrackRole.Lead when voice is not null && mix.VoiceVolumes?.TryGetValue(voice, out var own) == true
            => ClampVolume(own),
        AudioTrackRole.Lead => ClampVolume(mix.LeadVolume),
        _ => ClampVolume(mix.BackingVolume),
    };

    /// <inheritdoc cref="VolumeFor(AudioMix, AudioTrackRole, string?)"/>
    public static int VolumeFor(this AudioMix mix, AudioTrack track) => mix.VolumeFor(track.Role, track.Voice);
}
