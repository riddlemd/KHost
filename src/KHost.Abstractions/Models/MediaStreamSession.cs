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

    /// <summary>URLs of the unmixed stems, in audio-track order; empty unless the source had any.</summary>
    /// <remarks>Offered beside <see cref="PlaylistUrl"/> rather than instead of it: the stream is
    /// always produced, so a consumer that cannot mix is unaffected and one that can may ignore
    /// the stream. Entry <c>i</c> belongs to <c>AudioTrack.Index == i</c>.</remarks>
    public IReadOnlyList<string> StemUrls { get; init; } = [];
}
