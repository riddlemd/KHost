namespace KHost.Abstractions.Models;

/// <summary>What a file turned out to be: its length, its audio tracks, its container tags.</summary>
/// <remarks>Facts only. Whether two tracks are worth offering a host, or what an absent duration
/// means, is the asking service's policy and is deliberately not decided here: one probe feeds the
/// importer, the playback faders and the entitlement gate, and they do not want the same rules.
/// </remarks>
public sealed record MediaProbeResult
{
    /// <summary>How long it runs, or null when the probe could not say.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Its audio streams, already named and given roles by whoever understands the file.
    /// Empty for a file with nothing to separate, which is most of them.</summary>
    /// <remarks>Roled by the probe rather than by the host: a plugin knows its own stems outright,
    /// where the host can only guess from whatever the muxer happened to call them.</remarks>
    public IReadOnlyList<AudioTrack> AudioTracks { get; init; } = [];

    /// <summary>Container-level tags, matched without regard to case.</summary>
    /// <remarks>Tag names are case-preserving per muxer but effectively case-insensitive across
    /// them, so a reader must not depend on how one file happened to spell one.</remarks>
    public IReadOnlyDictionary<string, string> Tags { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
