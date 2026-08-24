using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services
{
    public abstract class BaseService
    {
        protected BaseService(ILogger logger)
        {
            Logger = logger;
        }

        protected ILogger Logger { get; }
    }
}
