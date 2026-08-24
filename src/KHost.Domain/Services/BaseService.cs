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


        // Not awaited: this says "redraw", and a publisher that waited for every component to
        // finish rendering would stall the show to do it.
        protected void Announce(object message)
        {
            if (Broker is { } broker)
                _ = broker.PublishAsync(message);
        }
    }
}
