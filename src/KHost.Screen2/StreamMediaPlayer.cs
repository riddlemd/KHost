using System.Text.Json;
using KHost.Abstractions.MediaPlayer;
using Microsoft.Extensions.Logging;

namespace KHost.Screen2;

/// <summary>
/// <see cref="IMediaPlayer"/> over an HTML <c>&lt;video&gt;</c> on the host's HLS stream. Nothing
/// is decoded here, so every property is a cache of what the page last reported.
/// </summary>
internal sealed class StreamMediaPlayer : IMediaPlayer
{
    private readonly ILogger<StreamMediaPlayer> _logger;
    private readonly Lock _lock = new();

    private IMediaPlayer.MediaInfo? _info;
    private TimeSpan _position;
    private TimeSpan _duration;
    private bool _isPlaying;
    private bool _isPaused;
    private float _volume = 1.0f;

    /// <summary>Song position the current stream's zero maps to; added to reported positions.</summary>
    private TimeSpan _streamStartOffset;

    /// <summary>This machine's clock plus this equals the host's. Near zero on the host's own box.</summary>
    private TimeSpan _clockOffset;

    private string? _backgroundUrl;
    private bool _backgroundPlaying;
    private float _backgroundVolume = 1f;

    /// <summary>Host-clock instant <see cref="_position"/> was sampled at, per the page's stamp.</summary>
    private DateTime? _sampledAtUtc;

    // Never raised: the browser owns rendering. Declared only to satisfy IMediaPlayer.
#pragma warning disable CS0067
    public event EventHandler<IMediaPlayer.FrameData>? FrameAvailable;
#pragma warning restore CS0067

    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>The background track played out. How the host knows to pick the next one.</summary>
    public event EventHandler? BackgroundEnded;

    /// <summary>Set by the host once the window exists; delivers a JSON command to the page.</summary>
    public Action<string>? SendToBrowser { get; set; }

    public StreamMediaPlayer(ILogger<StreamMediaPlayer> logger) => _logger = logger;

    public IMediaPlayer.MediaInfo? Info { get { lock (_lock) return _info; } }
    public bool IsLoaded { get { lock (_lock) return _info is not null; } }
    public bool IsPlaying { get { lock (_lock) return _isPlaying; } }
    public bool IsPaused { get { lock (_lock) return _isPaused; } }
    public TimeSpan Position { get { lock (_lock) return _position; } }
    public TimeSpan Duration { get { lock (_lock) return _duration; } }

    /// <summary>Host-clock instant <see cref="Position"/> was sampled at, or null before a report.</summary>
    public DateTime? SampledAtUtc { get { lock (_lock) return _sampledAtUtc; } }

    public float Volume
    {
        get { lock (_lock) return _volume; }
        set
        {
            lock (_lock) _volume = value;
            Send(new { type = "volume", value });
        }
    }

    /// <summary>Points the page at a host stream. Not a file load — nothing local is opened.</summary>
    public void LoadStream(string url, TimeSpan streamStartOffset)
    {
        lock (_lock)
        {
            _info = new IMediaPlayer.MediaInfo { FilePath = url };
            _streamStartOffset = streamStartOffset;
            _position = streamStartOffset;
            _duration = TimeSpan.Zero;
            _isPlaying = false;
            _isPaused = false;
        }

        _logger.LogInformation("Loading stream {Url} at offset {Offset}", url, streamStartOffset);
        Send(new { type = "load", url, autoplay = false });
    }

    /// <summary>Applied in the page: correction runs far more often than the IPC ticks.</summary>
    public void SetClockOffset(TimeSpan offset)
    {
        lock (_lock) _clockOffset = offset;

        _logger.LogInformation("Clock offset to host: {Offset}", offset);
        Send(new { type = "clock", offsetMs = offset.TotalMilliseconds });
    }

    /// <summary>Converted to stream time here, so the page never needs the song offset.</summary>
    public void SetTimeline(TimeSpan position, DateTime anchorUtc, bool isPlaying, bool isPrimary)
    {
        TimeSpan offset;
        lock (_lock) offset = _streamStartOffset;

        var withinStream = position - offset;

        Send(new
        {
            type = "timeline",
            position = Math.Max(0, withinStream.TotalSeconds),
            anchorEpochMs = (anchorUtc - DateTime.UnixEpoch).TotalMilliseconds,
            playing = isPlaying,
            primary = isPrimary,
        });
    }

    /// <summary>
    /// Points the second channel at a stream. No timeline and no correction: only the screen the
    /// room hears is sent any of this, so there is no group for it to stay in step with.
    /// </summary>
    public void LoadBackground(string url, bool autoPlay)
    {
        lock (_lock)
        {
            _backgroundUrl = url;
            _backgroundPlaying = autoPlay;
        }

        _logger.LogInformation("Loading background stream {Url}", url);
        Send(new { type = "bg-load", url, autoplay = autoPlay });
    }

    public void PlayBackground()
    {
        lock (_lock) _backgroundPlaying = true;
        Send(new { type = "bg-play" });
    }

    public void PauseBackground()
    {
        lock (_lock) _backgroundPlaying = false;
        Send(new { type = "bg-pause" });
    }

    public void StopBackground(TimeSpan? fadeDuration = null)
    {
        lock (_lock)
        {
            _backgroundUrl = null;
            _backgroundPlaying = false;
        }

        Send(new { type = "bg-stop", fadeMs = fadeDuration?.TotalMilliseconds ?? 0 });
    }

    public float BackgroundVolume
    {
        get { lock (_lock) return _backgroundVolume; }
        set
        {
            lock (_lock) _backgroundVolume = value;
            Send(new { type = "bg-volume", value });
        }
    }

    public string? BackgroundUrl { get { lock (_lock) return _backgroundUrl; } }
    public bool IsBackgroundPlaying { get { lock (_lock) return _backgroundPlaying; } }

    /// <summary>Blanks the picture. Playback continues, so the screen stays on the timeline.</summary>
    public void SetVideoEnabled(bool enabled)
    {
        _logger.LogInformation("Video {State}", enabled ? "on" : "blanked");
        Send(new { type = "video", enabled });
    }

    /// <summary>
    /// The host is the clock and the only way to stop this screen, so losing it pauses rather than
    /// letting the song run on unattended, and says so on the screen instead of looking frozen.
    /// </summary>
    public void SetHostLost(bool lost)
    {
        _logger.LogWarning("Host {State}", lost ? "lost" : "back");

        // The bed is paused with the song: nothing here can pick the next track or stop this one
        // while the host is away, so leaving it running would strand it playing to an empty desk.
        if (lost)
        {
            Pause();
            PauseBackground();
        }

        Send(new { type = "hostLost", lost });
    }

    /// <summary>Unused: IMediaPlayer declares it, but the host only ever sends a stream URL.</summary>
    public Task LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        _logger.LogError("Cannot load '{FilePath}': this screen only plays host streams", filePath);
        ErrorOccurred?.Invoke(this, "screen requires a host stream URL");

        return Task.CompletedTask;
    }

    public void Play() => Send(new { type = "play" });

    public void Pause() => Send(new { type = "pause" });

    public void Stop(TimeSpan? fadeDuration = null)
        => Send(new { type = "stop", fadeMs = (fadeDuration ?? TimeSpan.FromSeconds(5)).TotalMilliseconds });

    /// <summary>A move within the stream the page already holds — no transcode restart.</summary>
    public void Seek(TimeSpan position)
    {
        TimeSpan offset;
        lock (_lock) offset = _streamStartOffset;

        var within = position - offset;
        Send(new { type = "seek", position = Math.Max(0, within.TotalSeconds) });
    }

    /// <summary>Applies a status report from the page. Returns true if it was understood.</summary>
    public bool HandleBrowserMessage(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty)) return false;

            switch (typeProperty.GetString())
            {
                case "state":
                    _logger.LogDebug("<- browser {Json}", message);
                    lock (_lock)
                    {
                        var reported = TimeSpan.FromSeconds(root.GetProperty("position").GetDouble());
                        _position = _streamStartOffset + reported;
                        _isPlaying = root.GetProperty("playing").GetBoolean();
                        _isPaused = !_isPlaying && reported > TimeSpan.Zero;

                        // The page stamps in its own clock; the offset makes it host-comparable.
                        _sampledAtUtc = root.TryGetProperty("sampledAtEpochMs", out var stamp)
                            ? DateTime.UnixEpoch.AddMilliseconds(stamp.GetDouble()) + _clockOffset
                            : null;

                        // Unknown until the playlist gains an ENDLIST, so zero means "not yet".
                        if (root.TryGetProperty("duration", out var d) && d.GetDouble() > 0)
                            _duration = _streamStartOffset + TimeSpan.FromSeconds(d.GetDouble());
                    }
                    return true;

                case "ended":
                    lock (_lock) { _isPlaying = false; _isPaused = false; }
                    PlaybackEnded?.Invoke(this, EventArgs.Empty);
                    return true;

                // Kept off the song's state on purpose: routing this through "ended" would run the
                // singer's performance to completion because a bed track finished.
                case "bg-ended":
                    lock (_lock) _backgroundPlaying = false;
                    BackgroundEnded?.Invoke(this, EventArgs.Empty);
                    return true;

                case "error":
                    var text = root.TryGetProperty("message", out var m) ? m.GetString() ?? "unknown" : "unknown";
                    _logger.LogError("Player error: {Message}", text);
                    ErrorOccurred?.Invoke(this, text);
                    return true;

                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void Send(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        _logger.LogDebug("-> browser {Json}", json);
        SendToBrowser?.Invoke(json);
    }

    public void Dispose() { }
}
