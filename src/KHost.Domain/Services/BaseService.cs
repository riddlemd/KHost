using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services
{
    public abstract class BaseService : IKHostService
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

        public event EventHandler? StateChanged;

        protected void InvokeStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);

            // Not awaited, matching the event it stands beside: this says "redraw", and a publisher
            // that waited for every component to finish rendering would stall the show to do it.
            if (Broker is { } broker && StateChangedMessage is { } message)
                _ = broker.PublishAsync(message);
        }
    }
}
