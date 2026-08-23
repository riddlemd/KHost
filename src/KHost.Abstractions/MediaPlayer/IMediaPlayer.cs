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

    /// <summary>
    /// Raised on a background thread each time a decoded video frame is ready.
    /// The <see cref="FrameData"/> carries raw BGRA pixels; the handler must not
    /// hold a reference beyond the event call (the buffer may be reused).
    /// </summary>
    event EventHandler<FrameData>? FrameAvailable;

    event EventHandler? PlaybackEnded;

    /// <summary>The string carries the error message.</summary>
    event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// Probes <paramref name="filePath"/> with ffprobe and prepares it for playback.
    /// Must be called before <see cref="Play"/>.
    /// </summary>
    Task LoadAsync(string filePath, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// A single decoded video frame as a raw BGRA byte buffer.
    /// The pixel layout is 4 bytes per pixel: B, G, R, A (alpha always 255).
    /// </summary>
    public sealed class FrameData
    {
        /// <summary>Raw pixel bytes in BGRA order, <c>Width × Height × 4</c> bytes total.</summary>
        public byte[] Pixels { get; }

        public int Width { get; }
        public int Height { get; }

        /// <summary>Render opacity for this frame. 1.0 = fully opaque, 0.0 = fully transparent.</summary>
        public float Alpha { get; }

        public FrameData(byte[] pixels, int width, int height, float alpha = 1.0f)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            Alpha = alpha;
        }
    }
}
