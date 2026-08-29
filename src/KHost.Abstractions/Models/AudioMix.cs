namespace KHost.Abstractions.Models;

/// <summary>
/// How much of each voice rides on the music, for a file that ships them apart. The music itself
/// has no level here: it is the reference the two voices are set against, so moving it would only
/// move everything.
/// </summary>
public sealed record AudioMix(IReadOnlyList<AudioTrack> Tracks, int LeadVolume, int BackingVolume)
{
    public const int MinVolume = 0;
    public const int MaxVolume = 100;

    /// <summary>The singer is there to replace the lead, so it starts out of the way.</summary>
    public const int DefaultLeadVolume = 0;

    /// <summary>
    /// Only what the machine setting cannot answer for. Out of the way like the lead, because a
    /// harmony sung over a singer is still a voice competing with them; a room that wants the
    /// guide raises it, per venue or per song.
    /// </summary>
    public const int DefaultBackingVolume = 0;

    /// <summary>
    /// Whether there is anything to mix. One named voice is enough — a file may carry a lead
    /// without harmonies — but there is nothing to do without the music to set it against.
    /// </summary>
    public bool IsMixable =>
        Tracks.Any(t => t.Role == AudioTrackRole.Music)
        && Tracks.Any(t => t.Role is AudioTrackRole.Lead or AudioTrackRole.Backing);

    public bool Has(AudioTrackRole role) => Tracks.Any(t => t.Role == role);

    public static int Clamp(int volume) => Math.Clamp(volume, MinVolume, MaxVolume);
}
