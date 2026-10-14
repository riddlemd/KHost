using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Playback;

/// <summary>The song's playhead: interpolated between the display's reports, held while the screen
/// leads in, and stamped with the generation a conclusion claims.</summary>
/// <remarks>Owned by <see cref="PlaybackService"/>, which decides what a tick or a reached end
/// means; this only keeps the time and makes sure one song is retired once.</remarks>
internal sealed class PlaybackClock
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    /// <summary>How long after a load, seek or play an end the display reports is still taken to
    /// belong to what came before it.</summary>
    private static readonly TimeSpan EndedSettle = TimeSpan.FromSeconds(1);

    // Guards every field below but _ticking, and Position's writes made here.
    // _timer's swap is read-then-assign, reached from the UI dispatcher and Task.Run continuations at once;
    // unguarded, the loser's Timer becomes unreachable but keeps ticking, driving a second clock forever.
    private readonly Lock _lock = new();

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan> _displayStartTimeout;
    private readonly Func<double> _rate;
    private readonly Func<Task> _tick;

    private ITimer? _timer;

    // Interlocked only. A tick that outruns the interval must not overlap the next: two in flight
    // both see the song ended and rotate the singer away twice.
    private int _ticking;

    private DateTime _lastTick;

    // Moves on every reset and every conclusion. A conclusion claims the value it was raised under,
    // so the clock and the display's own end cannot both retire one song.
    private long _generation;

    // Claimed but not yet reset: the song still reads as Playing while the display stops, so an end
    // or tick landing then captures the claimed generation as live and would conclude it again.
    private bool _concluding;

    // The screen is holding the song before its start: the playhead sits at zero, not running on.
    private bool _holding;

    private DateTime _transportMovedAtUtc = DateTime.MinValue;

    // Set when a play goes out, cleared by the display's first playing report after it. A display
    // still fetching the song plays nothing, and a clock running meanwhile jumps back when it starts.
    private DateTime? _awaitingDisplaySince;

    /// <param name="displayStartTimeout">Read per use: the App Settings page writes it live.</param>
    /// <param name="rate">Song seconds per wall-clock second of the open stream.</param>
    /// <param name="tick">Run on each timer tick, never two at once.</param>
    public PlaybackClock(
        TimeProvider time,
        ILogger logger,
        Func<TimeSpan> displayStartTimeout,
        Func<double> rate,
        Func<Task> tick)
    {
        _time = time;
        _logger = logger;
        _displayStartTimeout = displayStartTimeout;
        _rate = rate;
        _tick = tick;
    }

    /// <summary>Song time. Written under the lock by the clock's own paths only; a caller's write
    /// is unguarded.</summary>
    public TimeSpan Position { get; set; }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <param name="restart">False leaves a running clock alone, checked and started as one step.</param>
    public void Start(bool restart = true)
    {
        lock (_lock)
        {
            if (!restart && _timer is not null) return;

            _lastTick = UtcNow;
            _timer?.Dispose();
            _timer = _time.CreateTimer(OnTick, null, Interval, Interval);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Spends the current generation, so a conclusion raised under it finds it gone.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _generation++;
            _concluding = false;
            _holding = false;
            _awaitingDisplaySince = null;
        }
    }

    public void MarkTransportMoved()
    {
        lock (_lock) _transportMovedAtUtc = UtcNow;
    }

    public void AwaitDisplayStart()
    {
        if (_displayStartTimeout() <= TimeSpan.Zero) return;

        lock (_lock) _awaitingDisplaySince = UtcNow;
    }

    /// <summary>The screen drops the rest of a hold on a seek and starts the song there.</summary>
    public void ReleaseHold()
    {
        lock (_lock) _holding = false;
    }

    /// <summary>Moves the playhead to where a playing report from the carrying display puts it.</summary>
    public void FollowReport(DisplayPlaybackStatus status)
    {
        var now = UtcNow;

        lock (_lock)
        {
            // Sampled before the play went out: the old stream still sounding, not this start.
            if (status.SampledAtUtc < _awaitingDisplaySince) return;

            // Sampled before the last seek, load or play: a report still in flight would drag the
            // playhead back to where the song was.
            if (status.SampledAtUtc < _transportMovedAtUtc) return;

            _awaitingDisplaySince = null;
            _holding = false;
            Position = status.Position + ((now - status.SampledAtUtc) * _rate());
            _lastTick = now;
        }
    }

    public void Hold(DisplayPlaybackStatus status)
    {
        lock (_lock)
        {
            _holding = true;
            Position = status.Position;
            _lastTick = UtcNow;
        }
    }

    /// <summary>Whether an end the carrying display reported belongs to the song now playing.</summary>
    /// <param name="generation">The generation to conclude, when it does.</param>
    public bool TryTakeEnd(DisplayPlaybackStatus status, out long generation)
    {
        lock (_lock)
        {
            generation = 0;

            if (_holding) return false;

            // Sampled before the last load, seek or play landed, or so soon after it that the
            // display cannot have reached an end since: the old stream's end, not this one's.
            if (status.SampledAtUtc < _transportMovedAtUtc + EndedSettle)
            {
                _logger.LogInformation("Ignored an end the display reported at {Position}, from before the last seek or load", status.Position);
                return false;
            }

            generation = _generation;
            return true;
        }
    }

    /// <summary>Claims the song playing under <paramref name="generation"/> for one conclusion.</summary>
    /// <returns>False when the other path already claimed it, or the song has moved on.</returns>
    public bool TryClaim(long generation)
    {
        lock (_lock)
        {
            // Claimed by moving the generation on, so the other path finds it spent.
            if (generation != _generation || _concluding) return false;
            _generation++;
            _concluding = true;
            return true;
        }
    }

    /// <summary>Moves the playhead on by the wall time since the last tick.</summary>
    /// <returns>False when the clock was stopped under a tick already running.</returns>
    public bool TryAdvance(out long generation)
    {
        lock (_lock)
        {
            generation = 0;

            // Timer.Dispose does not wait out a callback already running, so a tick can land after
            // a pause or stop, and would otherwise run a parked song to its end.
            if (_timer is null) return false;

            var now = UtcNow;

            // Position is song time and the clock is wall time, so a retimed song covers more or
            // less of itself per tick. A hold is the song not yet started, so it covers none.
            // Timed out: a display that never reports must still run the song out, from here.
            if (_awaitingDisplaySince is { } since && now - since >= _displayStartTimeout())
                _awaitingDisplaySince = null;

            if (!_holding && _awaitingDisplaySince is null)
                Position += (now - _lastTick) * _rate();
            _lastTick = now;
            generation = _generation;
            return true;
        }
    }

    private async void OnTick(object? state)
    {
        // Dropped, not queued: a tick only interpolates the playhead, and the one already running
        // has the newer reading anyway.
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;

        try
        {
            await _tick();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in playback tick");
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }
}
