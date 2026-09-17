namespace KHost.Abstractions.Messaging;

/// <summary>In-process publish/subscribe; publishing awaits every handler, unlike a void event.</summary>
public interface IMessageBroker
{
    /// <summary>Matched on the exact type; a base handler isn't called for a derived one.</summary>
    IDisposable Subscribe<TMessage>(Func<TMessage, CancellationToken, Task> handler) where TMessage : notnull;

    /// <summary>For a handler with no awaiting to do, such as one that only redraws.</summary>
    IDisposable Subscribe<TMessage>(Action<TMessage> handler) where TMessage : notnull;

    /// <summary>Returns once every handler finishes; a throwing handler is logged and skipped.</summary>
    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>Fire-and-forget, for "this moved, redraw"; await PublishAsync if outcome matters.</summary>
    void Announce<TMessage>(TMessage message) where TMessage : notnull;
}
