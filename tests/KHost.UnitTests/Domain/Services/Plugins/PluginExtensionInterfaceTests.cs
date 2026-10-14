using System.Reflection;
using KHost.Domain.Services.Plugins;
using KHost.UnitTests.Conventions;

namespace KHost.UnitTests.Domain.Services.Plugins;

/// <summary>The loader binds the interfaces marked <c>[PluginExtensionPoint]</c>, and a missing mark is
/// silent: the plugin loads, the interface is never bound, and the host behaves as though nothing
/// implemented it. That is how <c>IMediaProbe</c> once shipped implemented, tested and unreachable.
/// </summary>
public class PluginExtensionInterfaceTests
{
    /// <summary>A domain service taking <c>IEnumerable&lt;T&gt;</c> of an Abstractions interface is
    /// collecting plugin implementations of it, so the loader has to be binding it.</summary>
    [Fact]
    public void EveryInterfaceADomainServiceCollects_IsBoundByTheLoader()
    {
        var missing = CollectedInterfaces()
            .Where(type => !PluginLoader.ExtensionInterfaces.Contains(type))
            .Select(type => type.Name)
            .Order()
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Not marked [PluginExtensionPoint], so no plugin can ever supply one: {string.Join(", ", missing)}");
    }

    /// <summary>Pinned, so a mark removed by accident, or one put on a host service a plugin must not
    /// register over, fails here rather than in a running host.</summary>
    [Fact]
    public void TheMarkedExtensionPoints_AreExactlyThese()
    {
        Assert.Equal(
            [
                "IBreakMusicProvider", "IDisplayProvider", "IMediaPlaybackGate", "IMediaProbe", "IMediaProvider",
                "IMediaRefetcher", "IMediaRenderer", "IPlayableMediaSource", "IPluginButtonHandler",
                "IQueueRotationMode", "ITimedLyricsProvider",
            ],
            PluginLoader.ExtensionInterfaces.Select(type => type.Name).Order());
    }

    /// <summary>Guards the guard: a scan that matches nothing would pass the test above forever.
    /// </summary>
    [Fact]
    public void TheScan_FindsTheInterfacesItIsMeantTo()
    {
        var collected = CollectedInterfaces().Select(type => type.Name).ToList();

        Assert.Contains(nameof(KHost.Abstractions.Services.IMediaProbe), collected);
        Assert.Contains(nameof(KHost.Abstractions.Services.IMediaPlaybackGate), collected);
    }

    private static IReadOnlyList<Type> CollectedInterfaces()
        => HostAssemblies.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(type => type.GetGenericArguments()[0])
            .Where(type => type.IsInterface && type.Assembly == typeof(KHost.Abstractions.Services.IMediaProbe).Assembly)
            .Distinct()
            .ToList();
}
