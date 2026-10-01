namespace KHost.Abstractions.Models;

/// <summary>How a turn is to be sung: the values the song controls move while it plays, set
/// ahead of time on one still waiting.</summary>
/// <param name="Pitch">Semitones from the recording; held to the playback service's key range.</param>
/// <param name="Tempo">Percent either side of recorded speed; held to its tempo range.</param>
/// <param name="LeadVolume">Lead vocal level; held to <see cref="AudioMix.MinVolume"/>-<see cref="AudioMix.MaxVolume"/>.</param>
/// <param name="BackingVolume">Backing level; null leaves the machine setting answering.</param>
public sealed record PerformanceSettings(int Pitch, int Tempo, int LeadVolume, int? BackingVolume)
{
    /// <summary>Levels for named singers' leads, merged over those already saved: a voice left out
    /// keeps its level. Null changes none of them.</summary>
    public IReadOnlyDictionary<string, int>? VoiceVolumes { get; init; }
}
