namespace KHost.Plugins.Sdk.Messaging;

public static class MessageBrokerExtensions
{
    /// <summary>
    /// Publishes without waiting, for a message that only says "this moved, redraw". A publisher
    /// that waited for every component to finish rendering would stall the show to do it. Anything
    /// whose outcome the publisher depends on must await <see cref="IMessageBroker.PublishAsync"/>.
    /// </summary>
    public static void Announce<TMessage>(this IMessageBroker broker, TMessage message) where TMessage : notnull
        => _ = broker.PublishAsync(message);
}
