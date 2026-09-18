using System.Reflection;
using KHost.Domain.Services;
using KHost.Domain.Services.Plugins;

namespace KHost.UnitTests.Domain.Services.Plugins;

/// <summary>The loader binds a plugin's interfaces from a hand-written list, and leaving one off is
/// silent: the plugin loads, the interface is never bound, and the host behaves as though nothing
/// implemented it. That is how <c>IMediaProbe</c> shipped implemented, tested and unreachable.
/// </summary>
public class PluginExtensionInterfaceTests
{
    /// <summary>Collected from the host's own registrations, not from plugins. Binding it would let
    /// an installed plugin supply an <b>authentication</b> provider, which is a decision about what
    /// a plugin is trusted with rather than a list this test may quietly grow.</summary>
    private static readonly Type[] HostOnly = [typeof(KHost.Abstractions.Services.IAuthProvider)];

    /// <summary>A domain service taking <c>IEnumerable&lt;T&gt;</c> of an Abstractions interface is
    /// collecting plugin implementations of it, so the loader has to be binding it.</summary>
    [Fact]
    public void EveryInterfaceADomainServiceCollects_IsBoundByTheLoader()
    {
        var missing = CollectedInterfaces()
            .Where(type => !HostOnly.Contains(type))
            .Where(type => !PluginLoader.ExtensionInterfaces.Contains(type))
            .Select(type => type.Name)
            .Order()
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Not bound by PluginLoader.ExtensionInterfaces, so no plugin can ever supply one: {string.Join(", ", missing)}");
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
        => typeof(MediaProbeService).Assembly
            .GetTypes()
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
