namespace KHost.UserInterface.Services;

/// <summary>
/// Holds the current message and nothing else. How long it stays is a matter for whatever is
/// showing it — keeping the countdown out of here leaves this deterministic, and leaves the service
/// without a fire-and-forget timer running inside a singleton.
/// </summary>
public class FlashService : IFlashService
{
    public event EventHandler? StateChanged;

    public FlashMessage? Current { get; private set; }

    public void Show(string text, FlashKind kind = FlashKind.Success)
    {
        Current = new FlashMessage(text, kind);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dismiss()
    {
        if (Current is null) return;

        Current = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
