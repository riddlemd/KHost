namespace KHost.Abstractions.Models;

/// <summary>One unmixed stem, handed to a display that mixes them itself.</summary>
/// <remarks>The host still decides the levels — <see cref="Volume"/> carries the same gain the
/// host's own mix would otherwise have applied — so a display only applies what it is told and
/// never works out its own mixing policy.
///
/// <para><see cref="Index"/> matches <see cref="AudioTrack.Index"/>, so a stem and the track it came
/// from are the same thing named twice, not two lists to keep aligned.</para></remarks>
/// <param name="Index">Matches the source <see cref="AudioTrack.Index"/> this stem was split from.</param>
/// <param name="Role">Which voice this stem carries.</param>
/// <param name="Url">Where the display fetches this stem's audio.</param>
/// <param name="Volume">Gain against the music, 0-100; the music track itself has no level.</param>
public sealed record StemSource(int Index, AudioTrackRole Role, string Url, int Volume)
{
    /// <summary>The singer this stem's lead belongs to, matching <see cref="AudioTrack.Voice"/>;
    /// null for a lead no singer is named on and for every other role.</summary>
    /// <remarks>What a <see cref="StemLevel"/> is matched against, together with the role.</remarks>
    public string? Voice { get; init; }
}
