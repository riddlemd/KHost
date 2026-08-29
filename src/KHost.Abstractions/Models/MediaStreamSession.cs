namespace KHost.Abstractions.Models;

/// <summary>
/// One host-side transcode, addressable by any number of consumers as a single URL. Screens no
/// longer need to reach the media file itself — only this address.
/// </summary>
public sealed class MediaStreamSession
{
    public required string Id { get; init; }

    public required string SourcePath { get; init; }

    /// <summary>Absolute URL of the HLS playlist every consumer fetches.</summary>
    public required string PlaylistUrl { get; init; }

    /// <summary>
    /// Song position the stream's own zero maps to. A pitch change restarts the transcode
    /// part-way through a song, so a consumer's clock is only absolute once this is added.
    /// </summary>
    public required TimeSpan StartOffset { get; init; }

    /// <summary>Semitones; zero for the written key.</summary>
    public required int Pitch { get; init; }
}
