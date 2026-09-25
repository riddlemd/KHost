namespace KHost.Abstractions.Models;

/// <summary>How much of each voice rides on the music; the music has no level, it's the reference.</summary>
/// <param name="Tracks">The file's audio tracks, so a consumer can tell which roles it can mix.</param>
/// <param name="LeadVolume">Lead-vocal level, <see cref="MinVolume"/>-<see cref="MaxVolume"/>, for
/// every lead <see cref="VoiceVolumes"/> has no level of its own for.</param>
/// <param name="BackingVolume">Backing-vocal level, <see cref="MinVolume"/>-<see cref="MaxVolume"/>.</param>
public sealed record AudioMix(IReadOnlyList<AudioTrack> Tracks, int LeadVolume, int BackingVolume)
{
    /// <summary>The bottom of the volume range: silent.</summary>
    public const int MinVolume = 0;

    /// <summary>The top of the volume range: full level.</summary>
    public const int MaxVolume = 100;

    /// <summary>The singer is there to replace the lead, so it starts out of the way.</summary>
    public const int DefaultLeadVolume = 0;

    /// <summary>Harmonies are part of the song the singer is singing over, so they start at full.
    /// </summary>
    public const int DefaultBackingVolume = 100;

    /// <summary>Levels for the leads of named singers, keyed by <see cref="AudioTrack.Voice"/>.</summary>
    /// <remarks>A lead whose voice has an entry rides at it; every other lead — one with no voice,
    /// or a voice with no entry — rides at <see cref="LeadVolume"/>. Null is the same as empty.
    /// Keys match <see cref="AudioTrack.Voice"/> exactly.</remarks>
    public IReadOnlyDictionary<string, int>? VoiceVolumes { get; init; }

    /// <summary>Whether there's anything to mix: needs the music track plus a named voice.</summary>
    public bool IsMixable =>
        Tracks.Any(t => t.Role == AudioTrackRole.Music)
        && Tracks.Any(t => t.Role is AudioTrackRole.Lead or AudioTrackRole.Backing);

    /// <summary>Whether any track carries the given role.</summary>
    public bool Has(AudioTrackRole role) => Tracks.Any(t => t.Role == role);
}
