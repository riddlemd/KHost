using KHost.Plugins.Sdk.Messaging;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services
{
    public abstract class BaseService
    {
        protected BaseService(ILogger logger, IMessageBroker? broker = null)
        {
            Logger = logger;
            Broker = broker;
        }

        protected ILogger Logger { get; }

        protected IMessageBroker? Broker { get; }

        /// <summary>
        /// What this service publishes when it changes. Null for the services that never notified
        /// anyone — they inherit the event without ever raising it.
        /// </summary>
        protected virtual object? StateChangedMessage => null;

        // Not awaited: this says "redraw", and a publisher that waited for every component to
        // finish rendering would stall the show to do it.
        protected void InvokeStateChanged()
        {
            if (Broker is { } broker && StateChangedMessage is { } message)
                _ = broker.PublishAsync(message);
        }
    }
}
