using KHost.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Displays;

/// <summary>What the connected display decodes, which decides the codecs of every encode the host
/// opens for it.</summary>
public interface IDisplayCodecs
{
    /// <summary>The connected display decodes no H.264 or AAC; false with nothing connected.</summary>
    bool OpenCodecsOnly { get; }
}

/// <summary>Asks the connected display through <see cref="IDisplayProvider.DescribeTarget"/>.</summary>
/// <remarks>The providers are resolved on each read: a display provider takes the stream service, so
/// neither can take the other in its constructor.</remarks>
internal sealed class ConnectedDisplayCodecs(IServiceProvider services, ILogger<ConnectedDisplayCodecs> logger) : IDisplayCodecs
{
    public bool OpenCodecsOnly
    {
        get
        {
            try
            {
                return ConnectedDisplay.Find(services.GetServices<IDisplayProvider>()) is { Provider: var provider }
                       && provider.DescribeTarget()?.OpenCodecsOnly == true;
            }
            catch (Exception ex)
            {
                // A plugin display that cannot describe itself gets what every display used to.
                logger.LogWarning(ex, "Could not read what the connected display decodes; encoding H.264");
                return false;
            }
        }
    }
}
