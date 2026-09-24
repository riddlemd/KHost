namespace KHost.Abstractions.Messaging;

/// <summary>In-process publish/subscribe; publishing awaits every handler, unlike a void event.</summary>
/// <remarks>
/// <para>How a plugin hears what the show is doing, and how the host's services tell each other
/// something moved. A plugin takes this in its constructor; it never implements it. One broker
/// serves the whole host, and every call is safe from any thread.</para>
/// <para>Messages are the empty records in <see cref="Messages"/>, one per service, each naming a
/// fact (<see cref="Messages.PerformancesChanged"/>, <see cref="Messages.SelectedVenueChanged"/>,
/// <see cref="Messages.PluginTableChanged"/>). They carry no payload: re-read the service that
/// announced one.</para>
/// <para>Handlers of one message run one at a time, in subscription order. A handler that throws is
/// logged and skipped, and the rest still run. A handler subscribed while a message is being
/// delivered does not receive that message.</para>
/// <para>Never subscribe without disposing: a subscription that is never disposed keeps its handler,
/// and everything the handler reaches, alive for the life of the host. Collect them in a
/// <see cref="SubscriptionSet"/> and dispose it when the plugin or component goes away.</para>
/// </remarks>
public interface IMessageBroker
{
    /// <summary>Matched on the exact type; a base handler isn't called for a derived one.</summary>
    /// <returns>Disposing it unsubscribes; disposing twice is harmless.</returns>
    IDisposable Subscribe<TMessage>(Func<TMessage, CancellationToken, Task> handler) where TMessage : notnull;

    /// <summary>For a handler with no awaiting to do, such as one that only redraws.</summary>
    /// <remarks>Runs on the publisher's thread, so keep it short and never block in it.</remarks>
    /// <returns>Disposing it unsubscribes; disposing twice is harmless.</returns>
    IDisposable Subscribe<TMessage>(Action<TMessage> handler) where TMessage : notnull;

    /// <summary>Returns once every handler finishes; a throwing handler is logged and skipped.</summary>
    /// <remarks>For when the publisher's next decision depends on what the handlers did. Routed on
    /// the message's runtime type, not <typeparamref name="TMessage"/>.</remarks>
    /// <param name="message">The message handed to every handler subscribed to its type.</param>
    /// <param name="cancellationToken">Handed to each asynchronous handler. A handler that stops
    /// on it ends the publish with <see cref="OperationCanceledException"/>, and the handlers after
    /// it do not run.</param>
    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>Fire-and-forget, for "this moved, redraw"; await PublishAsync if outcome matters.</summary>
    /// <remarks>Handlers may start on the calling thread before this returns, so never announce while
    /// holding a lock a handler could need.</remarks>
    void Announce<TMessage>(TMessage message) where TMessage : notnull;
}
