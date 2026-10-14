namespace KHost.Domain.Services.Displays.LocalScreen;

/// <summary>Collects redraw requests for a settle window and draws what they add up to, one draw at
/// a time, so a burst of announcements from one host action lands as one draw of where it ended
/// rather than one per step, some of them mid-way.</summary>
/// <typeparam name="TFlags">A <see cref="FlagsAttribute"/> enum naming what is owed; requests merge by OR.</typeparam>
internal sealed class SettlingRedraw<TFlags> where TFlags : struct, Enum
{
    private readonly TimeSpan _settle;
    private readonly Func<TFlags, Task> _draw;
    private readonly Action<Exception> _failed;
    private readonly TimeProvider _time;

    private readonly Lock _gate = new();
    private long _owed;
    private bool _running;

    /// <param name="draw">Draws everything owed; one call at a time, never concurrently.</param>
    /// <param name="failed">Told of a draw that threw; the next request still draws.</param>
    public SettlingRedraw(TimeSpan settle, Func<TFlags, Task> draw, Action<Exception> failed, TimeProvider? time = null)
    {
        _settle = settle;
        _draw = draw;
        _failed = failed;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Owes <paramref name="flags"/> and returns at once; the draw is detached.</summary>
    public void Request(TFlags flags)
    {
        lock (_gate)
        {
            _owed |= Convert.ToInt64(flags);

            if (_running) return;
            _running = true;
        }

        _ = Task.Run(DrainAsync);
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            // Waited out before every draw, including one owed while the last was drawing: the
            // window has to start at a burst's first announcement or it splits the burst.
            await Task.Delay(_settle, _time);

            long owed;

            lock (_gate)
            {
                owed = _owed;
                _owed = 0;
            }

            try { await _draw((TFlags)Enum.ToObject(typeof(TFlags), owed)); }
            catch (Exception ex) { _failed(ex); }

            lock (_gate)
            {
                if (_owed == 0)
                {
                    _running = false;
                    return;
                }
            }
        }
    }
}
