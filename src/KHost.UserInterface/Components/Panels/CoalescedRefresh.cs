namespace KHost.UserInterface.Components.Panels;

/// <summary>Runs a panel's reload at most once at a time. Whatever is asked for while one runs is
/// merged into a single follow-up, so a burst of messages costs one or two reads, and the last
/// read to finish is always one that started after the newest request.</summary>
/// <remarks>Not thread-safe: call <see cref="RequestAsync"/> on the component's dispatcher (inside
/// <c>InvokeAsync</c>). A request made while a reload runs returns before its data is in.</remarks>
/// <typeparam name="TParts">A <c>[Flags]</c> enum naming what the reload can re-read.</typeparam>
public sealed class CoalescedRefresh<TParts>(Func<TParts, Task> refresh) where TParts : struct, Enum
{
    private ulong _pending;
    private bool _running;

    public async Task RequestAsync(TParts parts)
    {
        _pending |= Convert.ToUInt64(parts);
        if (_running) return;

        _running = true;
        try
        {
            while (_pending != 0)
            {
                var next = (TParts)Enum.ToObject(typeof(TParts), _pending);
                _pending = 0;
                await refresh(next);
            }
        }
        finally
        {
            _running = false;
        }
    }
}
