namespace KHost.Abstractions.Messaging;

/// <summary>
/// In-process publish/subscribe between the host's services and anything a plugin subscribes.
/// Publishing awaits every handler, which is the point of it: a C# event is void, so a publisher
/// that needs to know its handlers finished — the gap after a performance, held open until
/// whatever claims it has started — otherwise has to carry its own list of tasks to wait on.
/// </summary>
public interface IMessageBroker
{
    /// <summary>
    /// Matched on the exact message type: a handler for a base type is not called for a derived
    /// one, so what a subscription receives can be read off its own signature.
    /// </summary>
    IDisposable Subscribe<TMessage>(Func<TMessage, CancellationToken, Task> handler) where TMessage : notnull;

    /// <summary>For a handler with no awaiting to do, such as one that only redraws.</summary>
    IDisposable Subscribe<TMessage>(Action<TMessage> handler) where TMessage : notnull;

    /// <summary>
    /// Returns once every handler has finished. A handler that throws is logged and skipped rather
    /// than surfaced here — a broken subscriber must not stop the show for the rest.
    /// </summary>
    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>
    /// Publishes without waiting, for a message that only says "this moved, redraw". A publisher
    /// that waited for every component to finish rendering would stall the show to do it. Anything
    /// whose outcome the publisher depends on must await <see cref="PublishAsync"/> instead.
    /// </summary>
    void Announce<TMessage>(TMessage message) where TMessage : notnull;
}
