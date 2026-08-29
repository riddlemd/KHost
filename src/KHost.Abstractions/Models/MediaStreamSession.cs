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

    /// <summary>
    /// Percent either side of the recorded speed; zero as recorded. A consumer's clock runs in
    /// stream time, so song time is only recovered by scaling back through this.
    /// </summary>
    public required int Tempo { get; init; }

    /// <summary>Multiplier <see cref="Tempo"/> stands for. Stream seconds times this are song seconds.</summary>
    public double Rate => RateFor(Tempo);

    /// <summary>
    /// The one definition of what a tempo percentage means. Every consumer that keeps a clock —
    /// the host's, a screen's, a Cast receiver's — converts through it, and they must agree.
    /// </summary>
    public static double RateFor(int tempo) => 1.0 + (tempo / 100.0);
}
