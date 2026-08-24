namespace KHost.Abstractions.MediaPlayer;

/// <summary>
/// Platform-neutral — no dependency on System.Drawing or any OS-specific graphics API.
/// </summary>
public interface IMediaPlayer : IDisposable
{

    /// <summary>Metadata for the currently loaded file, or null if nothing is loaded.</summary>
    MediaInfo? Info { get; }

    bool IsLoaded { get; }
    bool IsPlaying { get; }
    bool IsPaused { get; }

    /// <summary>Current playback position, updated in real time while playing.</summary>
    TimeSpan Position { get; }

    /// <summary>Total duration of the loaded media. Zero if nothing is loaded.</summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Audio output gain.  0.0 = silent, 1.0 = unity (full volume).
    /// Values above 1.0 amplify beyond the recorded level.
    /// Always safe to set — no-op when the file has no audio or no device is available.
    /// </summary>
    float Volume { get; set; }

    event EventHandler? PlaybackEnded;

    /// <summary>Starts or resumes playback from the current position.</summary>
    void Play();

    /// <summary>Pauses playback, preserving the current position.</summary>
    void Pause();

    /// <summary>
    /// Stops playback and resets the position to the beginning.
    /// Audio and video fade out over <paramref name="fadeDuration"/> before the segment is torn down.
    /// Defaults to 5 seconds when not specified.
    /// </summary>
    void Stop(TimeSpan? fadeDuration = null);

    /// <summary>
    /// Seeks to <paramref name="position"/>.
    /// Continues playing if already playing; stays paused/stopped otherwise.
    /// </summary>
    void Seek(TimeSpan position);

    /// <summary>Metadata about a media file populated by ffprobe.</summary>
    public sealed class MediaInfo
    {
        public required string FilePath { get; init; }
        public string[] AuxiliaryFilePaths { get; init; } = [];
        public TimeSpan Duration { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public double Fps { get; init; }
        public int AudioSampleRate { get; init; }
        public int AudioChannels { get; init; }
        public bool HasVideo { get; init; }
        public bool HasAudio { get; init; }
        public bool StatefulFormat { get; init; }

        public override string ToString() =>
            $"{Path.GetFileName(FilePath)}  {Width}×{Height}  {Fps:F2} fps  {Duration:h\\:mm\\:ss}";
    }

}
