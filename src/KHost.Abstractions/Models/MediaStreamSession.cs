namespace KHost.Abstractions.Models;

/// <summary>One host-side encode, addressable by any number of consumers as a single URL.</summary>
public sealed class MediaStreamSession
{
    /// <summary>Identifies this session; part of the URLs consumers fetch through it.</summary>
    public required string Id { get; init; }

    /// <summary>Full path to the source file this session was opened for.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Absolute URL of the HLS playlist every consumer fetches.</summary>
    /// <remarks>Null when the session holds no encode at all — a directory opened so a renderer
    /// has somewhere served and swept to write its own files, with no host encode behind it.</remarks>
    public string? PlaylistUrl { get; init; }

    /// <summary>Where a renderer may write files that are served and swept with this session.</summary>
    /// <remarks>Reached over HTTP as <c>/media/{Id}/{fileName}</c>, and deleted when the session
    /// closes, so nothing written here outlives the song.</remarks>
    public string? WorkingDirectory { get; init; }

    /// <summary>Song position the stream's zero maps to; add it to get an absolute clock.</summary>
    public required TimeSpan StartOffset { get; init; }

    /// <summary>Semitones; zero for the written key.</summary>
    public required int Pitch { get; init; }

    /// <summary>Percent either side of recorded speed; scale by this to recover song time.</summary>
    public required int Tempo { get; init; }
}
