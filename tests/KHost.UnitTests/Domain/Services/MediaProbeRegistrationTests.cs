using KHost.Abstractions.Services;
using KHost.Domain;
using KHost.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.Domain.Services;

/// <summary>The probe fallback is registered keyed, which nothing else in the container is.</summary>
/// <remarks>A keyed registration is invisible to every other test: the service resolves fine when
/// constructed by hand, so losing the registration builds, passes, and then throws on the
/// <c>[FromKeyedServices]</c> parameter the first time a host starts.</remarks>
public class MediaProbeRegistrationTests
{
    [Fact]
    public void TheFallbackProbe_IsResolvableUnderTheKeyTheServiceAsksFor()
    {
        using var provider = Container();

        Assert.IsType<FfprobeMediaProbe>(
            provider.GetKeyedService<IMediaProbe>(MediaProbeService.FallbackKey));
    }

    /// <summary>It claims every file, so reached through the open registration it would answer for
    /// whatever a plugin registered after it.</summary>
    [Fact]
    public void TheFallbackProbe_IsNotOneOfThePluginProbes()
    {
        using var provider = Container();

        Assert.Empty(provider.GetServices<IMediaProbe>());
    }

    /// <summary>The failure this exists for: resolving the service is what binds the keyed
    /// parameter, and it throws rather than falling back when the key is not registered.</summary>
    [Fact]
    public void TheProbeService_Resolves()
        => Assert.IsType<MediaProbeService>(Container().GetRequiredService<IMediaProbeService>());

    /// <summary>Built from the host's own <c>AddDomain()</c>, not a container assembled here: a
    /// hand-built one would keep passing after the real registration was lost, which is the exact
    /// failure this is for.</summary>
    /// <remarks>Descriptors are inspected rather than resolved. Building the whole domain would
    /// need configuration, the database and the cache, none of which this question touches.
    /// </remarks>
    private static ServiceProvider Container()
    {
        var registered = new ServiceCollection().AddDomain();

        var services = new ServiceCollection();
        services.AddLogging();

        foreach (var descriptor in registered.Where(d => d.ServiceType == typeof(IMediaProbe)
                                                      || d.ServiceType == typeof(IMediaProbeService)))
        {
            ((IList<ServiceDescriptor>)services).Add(descriptor);
        }

        return services.BuildServiceProvider();
    }
}
