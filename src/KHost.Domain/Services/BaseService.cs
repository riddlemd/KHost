using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services
{
    public abstract class BaseService : IKHostService
    {
        protected BaseService(ILogger logger)
        {
            Logger = logger;
        }

        protected ILogger Logger { get; }

        public event EventHandler? StateChanged;

        protected void InvokeStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
