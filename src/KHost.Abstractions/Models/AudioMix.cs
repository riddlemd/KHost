namespace KHost.Abstractions.Models;

/// <summary>How much of each voice rides on the music; the music has no level, it's the reference.</summary>
/// <param name="Tracks">The file's audio tracks, so a consumer can tell which roles it can mix.</param>
/// <param name="LeadVolume">Lead-vocal level, <see cref="MinVolume"/>-<see cref="MaxVolume"/>.</param>
/// <param name="BackingVolume">Backing-vocal level, <see cref="MinVolume"/>-<see cref="MaxVolume"/>.</param>
public sealed record AudioMix(IReadOnlyList<AudioTrack> Tracks, int LeadVolume, int BackingVolume)
{
    /// <summary>The bottom of the volume range: silent.</summary>
    public const int MinVolume = 0;

    /// <summary>The top of the volume range: full level.</summary>
    public const int MaxVolume = 100;

    /// <summary>The singer is there to replace the lead, so it starts out of the way.</summary>
    public const int DefaultLeadVolume = 0;

    /// <summary>Out of the way like the lead; a room wanting the guide raises it per venue.</summary>
    public const int DefaultBackingVolume = 0;

    /// <summary>Whether there's anything to mix: needs the music track plus a named voice.</summary>
    public bool IsMixable =>
        Tracks.Any(t => t.Role == AudioTrackRole.Music)
        && Tracks.Any(t => t.Role is AudioTrackRole.Lead or AudioTrackRole.Backing);

    /// <summary>Whether any track carries the given role.</summary>
    public bool Has(AudioTrackRole role) => Tracks.Any(t => t.Role == role);
}
