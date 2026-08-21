namespace KHost.Domain.Services;

using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

/// <summary>
/// Holds the current message and nothing else. How long it stays is a matter for whatever shows it:
/// keeping the countdown out of here leaves this deterministic, and leaves a singleton without a
/// fire-and-forget timer running inside it.
/// </summary>
public class FlashService : IFlashService
{
    private FlashMessage? _current;

    public event EventHandler? StateChanged;

    public FlashMessage? Current => _current;

    public void Show(string text, FlashKind kind = FlashKind.Success)
    {
        _current = new FlashMessage(text, kind);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dismiss()
    {
        // Exchanged rather than tested and cleared: this is reachable from background work, and two
        // callers racing must not both announce the same withdrawal.
        if (Interlocked.Exchange(ref _current, null) is null) return;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
