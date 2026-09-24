namespace KHost.Abstractions.Models;

/// <summary>What a display should actually play for one song, and how it may be driven.</summary>
/// <remarks>Not always a stream. A renderer may answer with one URL to play end to end, with the
/// separate parts a display mixes for itself, or with both — the parts for a display that can take
/// them and the URL for one that cannot.</remarks>
public sealed class MediaRendition
{
    /// <summary>One thing the display plays end to end, when there is one to play.</summary>
    public string? Url { get; init; }

    /// <summary>Parts the display mixes itself; empty unless the renderer offered stems.</summary>
    public IReadOnlyList<StemSource> Stems { get; init; } = [];

    /// <summary>Where this rendition's zero sits in the song.</summary>
    /// <remarks>Non-zero for a stream cut at the playhead, which starts at 0 while the song is
    /// minutes in. Zero for anything carrying the whole song.</remarks>
    public TimeSpan StartOffset { get; init; }

    /// <summary>Percent either side of recorded speed; scale by this to recover song time.</summary>
    public int Tempo { get; init; }

    /// <summary>Semitones from the written key, which only a host-side encode can move.</summary>
    public int Pitch { get; init; }

    /// <summary>Whether a seek lands without rebuilding this.</summary>
    /// <remarks>False for a stream transcoded from the playhead, where seeking outside what has
    /// been written means opening a new one. True for a whole file or a set of parts, which are
    /// the entire song and can be seeked anywhere for free.</remarks>
    public bool SeekableInPlace { get; init; }

    /// <summary>The host-side encode behind this, or null when nothing was started for it.</summary>
    /// <remarks>Null is the point of the whole abstraction: a rendition the display plays directly
    /// starts no host encode, and there is then nothing to retire when the song ends. Whoever holds the
    /// rendition closes this; renderers do not clean up behind themselves.</remarks>
    public MediaStreamSession? Session { get; init; }
}
