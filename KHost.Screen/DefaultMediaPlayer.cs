using KHost.Abstractions.MediaPlayer;
using KHost.Screen.FFmpeg;
using KHost.Screen.OpenAl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace KHost.Screen;

/// <summary>
/// <see cref="IMediaPlayer"/> implementation that runs a <b>single</b> ffmpeg
/// process per playback segment.  The process outputs an interleaved AVI stream
/// (rawvideo BGRA + pcm_s16le) to stdout, which is demuxed in-process to drive
/// both the video-frame events and the OpenAL audio player.
/// </summary>
/// <remarks>
/// Audio playback requires an OpenAL-compatible device on the system
/// (openal32 on Windows, built-in framework on macOS, libopenal on Linux).
/// When unavailable the player still works — only video is rendered and
/// <see cref="Volume"/> is a no-op.
///
/// Seeking kills and restarts the ffmpeg process from the nearest keyframe.
/// </remarks>
public sealed class DefaultMediaPlayer : IMediaPlayer
{
    private readonly ILogger<DefaultMediaPlayer> _logger;
    private readonly IFfmpegService _ffmpeg;
    private readonly OpenAlAudioPlayer _audio = new();
    private IMediaPlayer.MediaInfo? _info;
    private State _state = State.Idle;
    private readonly object _lock = new();

    // Playback tracking
    private TimeSpan _startOffset;
    private DateTime _segmentWallStart;
    private bool _firstFrameSeen;

    // Single ffmpeg process + demux thread
    private Process? _process;
    private Thread? _demuxThread;
    private CancellationTokenSource? _cts;

    public event EventHandler<IMediaPlayer.FrameData>? FrameAvailable;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? ErrorOccurred;

    public IMediaPlayer.MediaInfo? Info => _info;
    public bool IsLoaded => _info is not null;
    public bool IsPlaying { get { lock (_lock) return _state == State.Playing; } }
    public bool IsPaused { get { lock (_lock) return _state == State.Paused; } }
    public TimeSpan Duration => _info?.Duration ?? TimeSpan.Zero;

    /// <inheritdoc/>
    public float Volume
    {
        get => _audio.Volume;
        set => _audio.Volume = value;
    }

    public TimeSpan Position
    {
        get
        {
            lock (_lock)
            {
                if (_state != State.Playing || !_firstFrameSeen)
                    return _startOffset;

                var pos = _startOffset + (DateTime.UtcNow - _segmentWallStart);
                return TimeSpan.FromTicks(Math.Clamp(pos.Ticks, 0, Duration.Ticks));
            }
        }
    }

    public DefaultMediaPlayer(IFfmpegService? ffmpegService = null, ILogger<DefaultMediaPlayer>? logger = null)
    {
        _logger = logger ?? NullLogger<DefaultMediaPlayer>.Instance;
        _ffmpeg = ffmpegService ?? new FfmpegService();
    }

    public async Task LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        StopAndReset();

        string json = await _ffmpeg
            .ProbeAsync(filePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _info = MediaInfoParser.Parse(filePath, json);

        lock (_lock) _state = State.Stopped;
    }

    public void Play()
    {
        lock (_lock)
        {
            if (_info is null) throw new InvalidOperationException("No file loaded. Call LoadAsync first.");
            if (_state == State.Playing) return;
            BeginSegment(_startOffset);
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_state != State.Playing) return;

            var captured = _firstFrameSeen
                ? TimeSpan.FromTicks(Math.Clamp(
                    (_startOffset + (DateTime.UtcNow - _segmentWallStart)).Ticks,
                    0, Duration.Ticks))
                : _startOffset;

            KillSegment();
            _startOffset = captured;
            _state = State.Paused;
        }
    }

    public void Stop()
    {
        StopAndReset();
        lock (_lock)
            _state = _info is not null ? State.Stopped : State.Idle;
    }

    public void Seek(TimeSpan position)
    {
        lock (_lock)
        {
            bool wasPlaying = _state == State.Playing;
            KillSegment();

            _startOffset = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, Duration.Ticks));

            if (wasPlaying)
                BeginSegment(_startOffset);
            else
                _state = _info is not null ? State.Stopped : State.Idle;
        }
    }

    public void Dispose()
    {
        StopAndReset();
        _audio.Dispose();
        _info = null;
    }

    // ── Segment management ───────────────────────────────────────────────────

    /// <summary>
    /// Starts a single ffmpeg process that outputs interleaved AVI
    /// (rawvideo BGRA + pcm_s16le) to stdout, then launches the demux thread.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private void BeginSegment(TimeSpan from)
    {
        if (_info!.FilePath == null || !File.Exists(_info!.FilePath))
            return;

        _startOffset = from;
        _firstFrameSeen = false;
        _cts = new CancellationTokenSource();

        // Build the ffmpeg argument list.
        var args = "-loglevel error";

        if (_info.StatefulFormat)
        {
            // Stateful formats (e.g. CDG) must decode from byte 0 on every input —
            // input-seeking jumps to a raw byte offset and corrupts the decoder.
            // All inputs decode from 0 (timestamps naturally 0-based), then output -ss
            // discards both streams inside ffmpeg before they reach the pipe so both
            // arrive aligned at T with no manual frame counting needed.
            foreach (var filePath in new[] { _info.FilePath }.Concat(_info.AuxiliaryFilePaths))
                args += $" -i \"{filePath}\"";

            if (from > TimeSpan.Zero)
                args += string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    " -ss {0:F3}", from.TotalSeconds);
        }
        else
        {
            args += string.Format(System.Globalization.CultureInfo.InvariantCulture, " -ss {0:F3}", from.TotalSeconds);

            foreach (var filePath in new[] { _info.FilePath }.Concat(_info.AuxiliaryFilePaths))
                args += $" -i \"{filePath}\"";
        }

        args += " -f avi";

        if (_info.HasVideo)
            args += " -c:v rawvideo -pix_fmt bgra";
        else
            args += " -vn";

        if (_info.HasAudio)
            args += " -c:a pcm_s16le";
        else
            args += " -an";

        args += " pipe:1";

        var psi = new ProcessStartInfo(_ffmpeg.FfmpegPath, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        _process = new Process { StartInfo = psi };
        _process.Start();
        _process.BeginErrorReadLine();

        // Prepare the audio player for the new stream's format.
        if (_info.HasAudio)
        {
            int sr = _info.AudioSampleRate > 0 ? _info.AudioSampleRate : 44100;
            int ch = Math.Max(1, _info.AudioChannels);
            _audio.Start(sr, ch);
        }

        var token = _cts.Token;
        _demuxThread = new Thread(() => DemuxProc(token))
        {
            IsBackground = true,
            Name = "FfmpegDemux",
        };
        _demuxThread.Start();
        _state = State.Playing;
    }

    /// <summary>
    /// Kills the ffmpeg process, stops audio, and waits for the demux thread.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private void KillSegment()
    {
        _cts?.Cancel();
        try { _process?.Kill(entireProcessTree: true); }
        catch (Exception ex) { _logger.LogWarning(ex, "Exception killing ffmpeg process"); }

        _audio.Stop();

        // Release the lock while joining so the demux thread can update state.
        System.Threading.Monitor.Exit(_lock);
        try { _demuxThread?.Join(2000); }
        finally { System.Threading.Monitor.Enter(_lock); }

        _process?.Dispose();
        _process = null;
        _demuxThread = null;
        _cts?.Dispose();
        _cts = null;
    }

    private void StopAndReset()
    {
        lock (_lock)
        {
            KillSegment();
            _startOffset = TimeSpan.Zero;
            _firstFrameSeen = false;
            _state = State.Idle;
        }
    }

    // ── Demux thread ────────────────────────────────────────────────────────

    /// <summary>
    /// Reads interleaved AVI chunks from the single ffmpeg process and
    /// dispatches video frames and audio PCM to their respective consumers.
    /// </summary>
    private void DemuxProc(CancellationToken token)
    {
        int w = _info!.Width;
        int h = _info.Height;
        int frameBytes = w * h * 4; // bgra
        var frameInterval = TimeSpan.FromSeconds(1.0 / (_info.Fps > 0 ? _info.Fps : 25));
        var stream = _process!.StandardOutput.BaseStream;
        var sw = Stopwatch.StartNew();
        TimeSpan nextFrameAt = TimeSpan.Zero;

        try
        {
            var demuxer = new AviDemuxer(stream);
            demuxer.SkipToMovi();

            while (!token.IsCancellationRequested)
            {
                var chunk = demuxer.ReadChunk();
                if (chunk is null) break; // end of stream

                if (chunk.IsVideo && chunk.Data.Length == frameBytes)
                {
                    // ── Video frame ─────────────────────────────────────────
                    if (!_firstFrameSeen)
                    {
                        lock (_lock)
                        {
                            _segmentWallStart = DateTime.UtcNow;
                            _firstFrameSeen = true;
                        }
                        sw.Restart();
                        nextFrameAt = TimeSpan.Zero;
                    }

                    // Throttle to the video's native frame rate.
                    var remaining = nextFrameAt - sw.Elapsed;
                    if (remaining > TimeSpan.FromMilliseconds(1))
                        Thread.Sleep(remaining);
                    nextFrameAt += frameInterval;

                    FrameAvailable?.Invoke(this, new IMediaPlayer.FrameData(chunk.Data, w, h));
                }
                else if (chunk.IsAudio)
                {
                    // ── Audio chunk → OpenAL ────────────────────────────────
                    _audio.FeedPcm(chunk.Data, chunk.Data.Length);
                }
            }
        }
        catch (EndOfStreamException) { /* normal pipe close after kill */ }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return;
        }

        // Flush any partial audio staging buffer.
        if (!token.IsCancellationRequested)
            _audio.Flush();

        if (!token.IsCancellationRequested)
        {
            lock (_lock)
            {
                _startOffset = TimeSpan.Zero;
                _state = State.Stopped;
            }
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private enum State { Idle, Stopped, Playing, Paused }
}
