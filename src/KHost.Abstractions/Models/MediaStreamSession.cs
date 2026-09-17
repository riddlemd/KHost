namespace KHost.Abstractions.Models;

/// <summary>One host-side transcode, addressable by any number of consumers as a single URL.</summary>
public sealed class MediaStreamSession
{
    public required string Id { get; init; }

    public required string SourcePath { get; init; }

    /// <summary>Absolute URL of the HLS playlist every consumer fetches.</summary>
    public required string PlaylistUrl { get; init; }

    /// <summary>Song position the stream's zero maps to; add it to get an absolute clock.</summary>
    public required TimeSpan StartOffset { get; init; }

    /// <summary>Semitones; zero for the written key.</summary>
    public required int Pitch { get; init; }

    /// <summary>Percent either side of recorded speed; scale by this to recover song time.</summary>
    public required int Tempo { get; init; }
}
