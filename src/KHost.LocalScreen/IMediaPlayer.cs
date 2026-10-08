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

    /// <summary>What is known about the loaded media.</summary>
    internal sealed class MediaInfo
    {
        /// <summary>The path or URL the player loaded.</summary>
        public required string FilePath { get; init; }
    }

}
