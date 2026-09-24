namespace KHost.LocalScreen;

/// <summary>The player that puts the song out of a screen: transport, position and level.</summary>
/// <remarks>
/// Platform-neutral: no dependency on System.Drawing or any OS-specific graphics API. The
/// transport members ask; the state properties follow once the player reports back, so reading
/// <see cref="IsPlaying"/> straight after <see cref="Play"/> may still say false.
/// </remarks>
internal interface IMediaPlayer : IDisposable
{

    /// <summary>What is known about the loaded media, or null if nothing is loaded.</summary>
    MediaInfo? Info { get; }

    /// <summary>Whether media is loaded, playing or not.</summary>
    bool IsLoaded { get; }

    /// <summary>Whether the media is playing now.</summary>
    bool IsPlaying { get; }

    /// <summary>Whether playback is stopped part-way through, so <see cref="Play"/> resumes it.</summary>
    bool IsPaused { get; }

    /// <summary>Current playback position in the song, as last reported while playing.</summary>
    TimeSpan Position { get; }

    /// <summary>Total duration of the loaded media; zero when nothing is loaded or the length is not
    /// known yet.</summary>
    TimeSpan Duration { get; }

    /// <summary>Output level: 0.0 silent to 1.0 full.</summary>
    float Volume { get; set; }

    /// <summary>The loaded media played to its end; not raised by <see cref="Stop"/>.</summary>
    event EventHandler? PlaybackEnded;

    /// <summary>Starts or resumes playback from the current position.</summary>
    void Play();

    /// <summary>Pauses playback, preserving the current position.</summary>
    void Pause();

    /// <summary>Stops playback, fading out over <paramref name="fadeDuration"/>.</summary>
    /// <param name="fadeDuration">Null uses the player's own default fade.</param>
    void Stop(TimeSpan? fadeDuration = null);

    /// <summary>Seeks to <paramref name="position"/>; stays playing or paused, whichever it was.</summary>
    void Seek(TimeSpan position);

    /// <summary>What is known about the loaded media; a value not known is zero, false or empty.</summary>
    internal sealed class MediaInfo
    {
        /// <summary>The path or URL the player loaded.</summary>
        public required string FilePath { get; init; }
        /// <summary>Companion files played alongside <see cref="FilePath"/>, such as the audio half of a
        /// CD+G pair; empty when the media is one file.</summary>
        public string[] AuxiliaryFilePaths { get; init; } = [];
        /// <summary>Length of the media; zero when not known.</summary>
        public TimeSpan Duration { get; init; }
        /// <summary>Picture width in pixels; zero when there is no picture or it is not known.</summary>
        public int Width { get; init; }
        /// <summary>Picture height in pixels; zero when there is no picture or it is not known.</summary>
        public int Height { get; init; }
        /// <summary>Picture frame rate in frames per second; zero when not known.</summary>
        public double Fps { get; init; }
        /// <summary>Audio sample rate in hertz; zero when there is no audio or it is not known.</summary>
        public int AudioSampleRate { get; init; }
        /// <summary>Number of audio channels; zero when there is no audio or it is not known.</summary>
        public int AudioChannels { get; init; }
        /// <summary>Whether the media carries a picture.</summary>
        public bool HasVideo { get; init; }
        /// <summary>Whether the media carries sound.</summary>
        public bool HasAudio { get; init; }
        /// <summary>Whether the format decodes only from its start, so a seek cannot jump straight to a
        /// position; CD+G graphics are such a format.</summary>
        public bool StatefulFormat { get; init; }

        /// <summary>A one-line description for logs: file name, picture size, frame rate and length.</summary>
        public override string ToString() =>
            $"{Path.GetFileName(FilePath)}  {Width}×{Height}  {Fps:F2} fps  {Duration:h\\:mm\\:ss}";
    }

}
