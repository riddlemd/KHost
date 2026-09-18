using KHost.Abstractions.Models;
using KHost.Abstractions.Services.IPC;
using System.Text.Json;
using KHost.Abstractions.MediaPlayer;
using Microsoft.Extensions.Logging;
using KHost.Common.Media;

namespace KHost.Screen2;

/// <summary>IMediaPlayer over an HTML &lt;video&gt; on the host's HLS stream.</summary>
/// <remarks>Nothing is decoded here; every property is a cache of what the page last reported.</remarks>
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

    /// <summary>Stream seconds times this are song seconds. The page never learns of it.</summary>
    private double _rate = 1.0;

    /// <summary>This machine's clock plus this equals the host's. Near zero on the host's own box.</summary>
    private TimeSpan _clockOffset;

    private string? _backgroundUrl;
    private bool _backgroundPlaying;
    private float _backgroundVolume = 1f;
    private string? _stillUrl;

    /// <summary>Host-clock instant <see cref="_position"/> was sampled at, per the page's stamp.</summary>
    private DateTime? _sampledAtUtc;

    public event EventHandler? PlaybackEnded;

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

    /// <summary>Points the page at a host stream rather than a file load: nothing local is opened.</summary>
    public void LoadStream(string url, TimeSpan streamStartOffset, int tempo = 0)
    {
        lock (_lock)
        {
            _info = new IMediaPlayer.MediaInfo { FilePath = url };
            _streamStartOffset = streamStartOffset;
            _rate = StreamRate.FromTempo(tempo);
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

    /// <summary>Converted to stream time here so the page never needs the song offset or tempo.</summary>
    /// <remarks>A retimed stream still advances one stream second per second, all the page assumes.</remarks>
    public void SetTimeline(TimeSpan position, DateTime anchorUtc, bool isPlaying, bool isPrimary)
    {
        TimeSpan offset;
        double rate;
        lock (_lock) { offset = _streamStartOffset; rate = _rate; }

        var withinStream = (position - offset) / rate;

        Send(new
        {
            type = "timeline",
            position = Math.Max(0, withinStream.TotalSeconds),
            anchorEpochMs = (anchorUtc - DateTime.UnixEpoch).TotalMilliseconds,
            playing = isPlaying,
            primary = isPrimary,
        });
    }

    /// <summary>Points the second channel at a stream with no timeline and no correction.</summary>
    /// <remarks>Only the screen the room hears gets this, so nothing needs to stay in step with it.</remarks>
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

    /// <summary>Puts a still up: nothing opens or plays, so only the host clock takes it down.</summary>
    public void ShowImage(string url, ImageScaling scaling)
    {
        lock (_lock) _stillUrl = url;

        _logger.LogInformation("Showing still {Url} scaled {Scaling}", url, scaling);

        // Lowercased here rather than in the page: the page should not have to know the enum's
        // spelling, only the CSS word it maps to.
        Send(new { type = "show-image", url, scaling = scaling.ToString().ToLowerInvariant() });
    }

    public void HideImage()
    {
        lock (_lock) _stillUrl = null;

        Send(new { type = "hide-image" });
    }

    /// <summary>The whole band in one message.</summary>
    /// <remarks>Recomputed every queue/venue change, sent complete: no partial state to keep.</remarks>
    public void SetMarquee(SetMarqueeCommand command)
    {
        _logger.LogInformation("Marquee {State} with {Count} singer(s)",
            command.Enabled ? "on" : "off", command.Singers.Count);

        // Lowercased here rather than in the page, the same as show-image's scaling: the page
        // knows CSS words, not this enum's spelling.
        Send(new
        {
            type = "marquee",
            enabled = command.Enabled,
            singers = command.Singers,
            message = command.Message,
            position = command.Position.ToString().ToLowerInvariant(),
            backgroundColor = command.BackgroundColor,
            textColor = command.TextColor,
            fontSizePixels = command.FontSizePixels,
            scrollSpeed = command.ScrollSpeed,
            pinLabel = command.PinLabel,
        });
    }

    /// <summary>What is playing between singers. Disabled carries nothing, so the card drops.</summary>
    public void SetBreakMusicCard(SetBreakMusicCardCommand command)
    {
        _logger.LogInformation("Break music card: {State}", command.Enabled ? command.Title : "off");

        Send(new
        {
            type = "break-music-card",
            enabled = command.Enabled,
            title = command.Title,
            artist = command.Artist,

            // Lowercased here rather than in the page, the same as the codes': the page knows CSS
            // words, not this enum's spelling.
            corner = command.Corner.ToString().ToLowerInvariant(),
            offset = command.Offset,
        });
    }

    /// <summary>Puts who is up on the screen, in place of the venue's card.</summary>
    /// <remarks>Nothing takes it down: the next thing drawn replaces it, which is how it survives
    /// a host who announces and then waits.</remarks>
    public void ShowNextSinger(ShowNextSingerCommand command)
    {
        _logger.LogInformation("Next singer card: {Singer}", command.Singer);

        Send(new
        {
            type = "next-singer",
            singer = command.Singer,
            song = command.Song,
            artist = command.Artist,
        });
    }

    /// <summary>Every code at once, the same whole-state push as the marquee.</summary>
    /// <remarks>An empty list is how they come down: no separate hide to keep in step.</remarks>
    public void SetQrCodes(SetScreenQrCodesCommand command)
    {
        _logger.LogInformation("QR codes: {Count}", command.Codes.Count);

        // Lowercased here rather than in the page, the same as show-image's scaling: the page
        // knows CSS words, not these enums' spelling.
        Send(new
        {
            type = "qr-codes",
            codes = command.Codes.Select(code => new
            {
                imageUrl = code.ImageUrl,
                modules = code.Modules,
                caption = code.Caption,
                corner = code.Corner.ToString().ToLowerInvariant(),
                size = code.Size.ToString().ToLowerInvariant(),

                // Forwarded, not re-derived: the page's CSS falls back to the same numbers the host
                // resolves to, so a venue at zero looked right while dropping these silently did nothing.
                safeZone = code.SafeZone,
                offset = code.Offset,
            }),
        });
    }

    public string? StillUrl { get { lock (_lock) return _stillUrl; } }

    /// <summary>Blanks the picture. Playback continues, so the screen stays on the timeline.</summary>
    public void SetVideoEnabled(bool enabled)
    {
        _logger.LogInformation("Video {State}", enabled ? "on" : "blanked");
        Send(new { type = "video", enabled });
    }

    /// <summary>The host is the clock and the only way to stop this screen.</summary>
    /// <remarks>Losing it pauses rather than running unattended, and says so instead of frozen.</remarks>
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

    public void Play() => Send(new { type = "play" });

    public void Pause() => Send(new { type = "pause" });

    public void Stop(TimeSpan? fadeDuration = null)
        => Send(new { type = "stop", fadeMs = (fadeDuration ?? TimeSpan.FromSeconds(5)).TotalMilliseconds });

    /// <summary>A move within the stream the page already holds: no transcode restart.</summary>
    public void Seek(TimeSpan position)
    {
        TimeSpan offset;
        double rate;
        lock (_lock) { offset = _streamStartOffset; rate = _rate; }

        var within = (position - offset) / rate;
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
                        _position = _streamStartOffset + (reported * _rate);
                        _isPlaying = root.GetProperty("playing").GetBoolean();
                        _isPaused = !_isPlaying && reported > TimeSpan.Zero;

                        // The page stamps in its own clock; the offset makes it host-comparable.
                        _sampledAtUtc = root.TryGetProperty("sampledAtEpochMs", out var stamp)
                            ? DateTime.UnixEpoch.AddMilliseconds(stamp.GetDouble()) + _clockOffset
                            : null;

                        // Unknown until the playlist gains an ENDLIST, so zero means "not yet".
                        if (root.TryGetProperty("duration", out var d) && d.GetDouble() > 0)
                            _duration = _streamStartOffset + (TimeSpan.FromSeconds(d.GetDouble()) * _rate);
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
