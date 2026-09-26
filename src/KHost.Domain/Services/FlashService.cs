namespace KHost.Domain.Services;

using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;

/// <summary>Holds the stack of showing messages and nothing else; the countdown stays out of here.</summary>
/// <remarks>Deterministic, with no timer running inside a singleton. Reachable from background
/// work, so the list is guarded by a lock that is released before every publish.</remarks>
public class FlashService : IFlashService
{
    private readonly IMessageBroker _broker;
    private readonly object _gate = new();
    private readonly List<FlashMessage> _messages = [];

    public FlashMessage? Current
    {
        get { lock (_gate) return _messages.Count > 0 ? _messages[^1] : null; }
    }

    public IReadOnlyList<FlashMessage> Messages
    {
        get { lock (_gate) return [.. _messages]; }
    }

    public FlashService(IMessageBroker broker)
    {
        _broker = broker;
    }

    public void Show(string text, FlashType type = FlashType.Success)
    {
        lock (_gate) _messages.Add(new FlashMessage(text, type));

        _ = _broker.PublishAsync(new FlashChanged());
    }

    public void Dismiss()
    {
        FlashMessage message;

        lock (_gate)
        {
            if (_messages.Count == 0) return;

            message = _messages[^1];
            _messages.RemoveAt(_messages.Count - 1);
        }

        _ = _broker.PublishAsync(new FlashChanged());
    }

    public void Dismiss(FlashMessage message)
    {
        bool removed;

        // Id-based, not the reference: a caller can hold a copy of the record it read earlier.
        lock (_gate) removed = _messages.RemoveAll(m => m.Id == message.Id) > 0;

        if (!removed) return;

        _ = _broker.PublishAsync(new FlashChanged());
    }
}
