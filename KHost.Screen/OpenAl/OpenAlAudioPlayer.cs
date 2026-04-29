using Microsoft.Extensions.Logging;
using static KHost.Screen.OpenAl.OpenAlNative;

namespace KHost.Screen.OpenAl;

/// <summary>
/// Receives raw s16le PCM audio via <see cref="FeedPcm"/> and streams it into an
/// OpenAL source using a ring of buffers.  No extra process is spawned — audio
/// data is pushed in from the caller's thread (typically the demux thread).
/// When no OpenAL device is found, <see cref="IsAvailable"/> is false and every
/// method becomes a no-op.
/// </summary>
internal sealed class OpenAlAudioPlayer : IDisposable
{
    private const int RingSize = 4;
    private const int StagingSamples = 4096; // samples per channel per staging flush

    private readonly ILogger<OpenAlAudioPlayer> _logger;

    // OpenAL handles
    private readonly IntPtr _device;
    private readonly IntPtr _context;
    private int _source;
    private int[] _bufferPool = Array.Empty<int>();

    // Ring-buffer tracking
    private readonly Queue<int> _free = new();

    // Staging: accumulates small PCM pushes before submitting to OpenAL.
    private byte[]? _staging;
    private int _stagingPos;
    private int _stagingCapacity;

    // Audio format (set by Start)
    private int _alFormat;
    private int _sampleRate;
    private bool _started;

    private volatile float _volume = 1.0f;

    // ── Public API ──────────────────────────────────────────────────────────

    public bool IsAvailable { get; }

    /// <summary>
    /// Audio gain: 0.0 = silent, 1.0 = unity, &gt;1.0 amplifies.
    /// Takes effect immediately.
    /// </summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Max(0f, value);
            if (IsAvailable && _started)
                alSourcef(_source, AL_GAIN, _volume);
        }
    }

    // ── Construction ────────────────────────────────────────────────────────

    public OpenAlAudioPlayer(ILogger<OpenAlAudioPlayer> logger)
    {
        _logger = logger;

        if (!OpenAlNative.IsLoaded)
        {
            if (OpenAlNative.LoadException is not null)
                _logger.LogWarning(OpenAlNative.LoadException, "OpenAL native load failed; audio will be disabled");
            else
                _logger.LogInformation("OpenAL native library not present; audio will be disabled");
            return;
        }

        try
        {
            _device = alcOpenDevice(null);
            if (_device == IntPtr.Zero)
            {
                _logger.LogWarning("alcOpenDevice returned null; audio disabled");
                return;
            }
            _context = alcCreateContext(_device, IntPtr.Zero);
            if (_context == IntPtr.Zero)
            {
                _logger.LogWarning("alcCreateContext returned null; audio disabled");
                return;
            }
            alcMakeContextCurrent(_context);

            alGenSources(1, out _source);
            _bufferPool = new int[RingSize];
            alGenBuffers(RingSize, _bufferPool);

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAL device init failed; audio disabled");
        }
    }

    // ── Streaming lifecycle ─────────────────────────────────────────────────

    /// <summary>
    /// Prepares the player for a new stream of PCM data at the given format.
    /// Must be called before <see cref="FeedPcm"/>.
    /// </summary>
    public void Start(int sampleRate, int channels)
    {
        if (!IsAvailable) return;
        Stop();

        _alFormat = channels == 1 ? AL_FORMAT_MONO16 : AL_FORMAT_STEREO16;
        _sampleRate = sampleRate;
        _stagingCapacity = StagingSamples * channels * sizeof(short);
        _staging = new byte[_stagingCapacity];
        _stagingPos = 0;
        _started = true;

        _free.Clear();
        foreach (int bid in _bufferPool)
            _free.Enqueue(bid);

        alSourcef(_source, AL_GAIN, _volume);
    }

    /// <summary>
    /// Pushes raw s16le PCM bytes into the player.  Data is accumulated in a
    /// staging buffer and flushed to OpenAL when full.  May block briefly while
    /// waiting for a free ring buffer (natural back-pressure).
    /// </summary>
    public void FeedPcm(byte[] data, int byteCount)
    {
        if (!IsAvailable || !_started || byteCount == 0) return;

        int offset = 0;
        while (offset < byteCount)
        {
            int space = _stagingCapacity - _stagingPos;
            int copy = Math.Min(space, byteCount - offset);
            Buffer.BlockCopy(data, offset, _staging!, _stagingPos, copy);
            _stagingPos += copy;
            offset += copy;

            if (_stagingPos >= _stagingCapacity)
                FlushStaging();
        }
    }

    /// <summary>Flushes any remaining staging data (e.g. at end-of-stream).</summary>
    public void Flush()
    {
        if (!IsAvailable || !_started || _stagingPos == 0) return;
        FlushStaging();
    }

    /// <summary>Stops playback and unqueues all OpenAL buffers.</summary>
    public void Stop()
    {
        if (!IsAvailable) return;
        alSourceStop(_source);
        UnqueueAll();
        _stagingPos = 0;
        _started = false;
    }

    public void Dispose()
    {
        Stop();
        if (!IsAvailable) return;
        alDeleteBuffers(_bufferPool.Length, _bufferPool);
        alDeleteSources(1, ref _source);
        alcDestroyContext(_context);
        alcCloseDevice(_device);
    }

    // ── Internal ────────────────────────────────────────────────────────────

    private void FlushStaging()
    {
        int bid = AcquireBuffer();

        // alBufferData reads exactly 'size' bytes from the array.
        alBufferData(bid, _alFormat, _staging!, _stagingPos, _sampleRate);
        alSourceQueueBuffers(_source, 1, ref bid);

        // Ensure the source is playing.
        alGetSourcei(_source, AL_SOURCE_STATE, out int state);
        if (state != AL_PLAYING)
            alSourcePlay(_source);

        _stagingPos = 0;
    }

    /// <summary>
    /// Returns a free buffer ID, blocking briefly if all buffers are in flight.
    /// </summary>
    private int AcquireBuffer()
    {
        while (_free.Count == 0)
        {
            alGetSourcei(_source, AL_BUFFERS_PROCESSED, out int done);
            while (done-- > 0)
            {
                alSourceUnqueueBuffers(_source, 1, out int freed);
                _free.Enqueue(freed);
            }
            if (_free.Count == 0)
                Thread.Sleep(5);
        }
        return _free.Dequeue();
    }

    private void UnqueueAll()
    {
        _free.Clear();
        alGetSourcei(_source, AL_BUFFERS_QUEUED, out int queued);
        while (queued-- > 0)
        {
            try
            {
                alSourceUnqueueBuffers(_source, 1, out int bid);
                _free.Enqueue(bid);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "alSourceUnqueueBuffers threw during UnqueueAll");
                break;
            }
        }
        // Return any pool buffers that were never queued.
        foreach (int bid in _bufferPool)
            if (!_free.Contains(bid))
                _free.Enqueue(bid);
    }
}
