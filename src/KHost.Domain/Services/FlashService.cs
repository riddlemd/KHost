namespace KHost.Domain.Services;

using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;

/// <summary>
/// Holds the current message and nothing else. How long it stays is a matter for whatever shows it:
/// keeping the countdown out of here leaves this deterministic, and leaves a singleton without a
/// fire-and-forget timer running inside it.
/// </summary>
public class FlashService : IFlashService
{
    private readonly IMessageBroker _broker;

    private FlashMessage? _current;


    public FlashMessage? Current => _current;

    public FlashService(IMessageBroker broker)
    {
        _broker = broker;
    }

    public void Show(string text, FlashType type = FlashType.Success)
    {
        _current = new FlashMessage(text, type);
        if (_broker is { } broker)
            _ = broker.PublishAsync(new FlashChanged());
    }

    public void Dismiss()
    {
        // Exchanged rather than tested and cleared: this is reachable from background work, and two
        // callers racing must not both announce the same withdrawal.
        if (Interlocked.Exchange(ref _current, null) is null) return;

        if (_broker is { } broker)
            _ = broker.PublishAsync(new FlashChanged());
    }
}
