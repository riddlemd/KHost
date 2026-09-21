using KHost.Domain;
using KHost.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

/// <summary>The listening address is not known until Kestrel is up, so it lands on options that
/// were bound before it existed.</summary>
/// <remarks><c>IOptions</c> and <c>IOptionsMonitor</c> keep separate caches, so the startup
/// resolution used to reach only the first. <c>HlsMediaStreamService</c> reads the monitor, and
/// went on handing screens a stream URL on the default port 5000 — which nothing listens on, so
/// the video element never loaded and the screen stayed black with no error anywhere.</remarks>
public class ResolvedHostAddressTests
{
    private const string ResolvedBase = "http://localhost:5251";
    private const string ResolvedIpc = "http://localhost:5251/ipc/screen";

    [Fact]
    public void MediaStreamBaseAddress_WhenNothingResolved_KeepsTheCompileTimeDefault()
    {
        using var provider = Container();

        Assert.Equal(
            "http://localhost:5000",
            provider.GetRequiredService<IOptionsMonitor<HlsMediaStreamService.ServiceOptions>>()
                .CurrentValue.BaseAddress);
    }

    /// <summary>The regression itself: the monitor is read after the address is resolved.</summary>
    [Fact]
    public void MediaStreamBaseAddress_WhenResolvedBeforeTheFirstRead_ReachesTheMonitor()
    {
        using var provider = Container();

        provider.GetRequiredService<ResolvedHostAddress>().MediaStreamBaseAddress = ResolvedBase;

        Assert.Equal(
            ResolvedBase,
            provider.GetRequiredService<IOptionsMonitor<HlsMediaStreamService.ServiceOptions>>()
                .CurrentValue.BaseAddress);
    }

    /// <summary>A reader that got in first pins the default in the monitor's cache, which is why
    /// the resolution clears it.</summary>
    [Fact]
    public void MediaStreamBaseAddress_WhenAlreadyCachedAtTheDefault_ReachesTheMonitorAfterTheCacheIsCleared()
    {
        using var provider = Container();
        var monitor = provider.GetRequiredService<IOptionsMonitor<HlsMediaStreamService.ServiceOptions>>();

        Assert.Equal("http://localhost:5000", monitor.CurrentValue.BaseAddress);

        provider.GetRequiredService<ResolvedHostAddress>().MediaStreamBaseAddress = ResolvedBase;
        provider.GetRequiredService<IOptionsMonitorCache<HlsMediaStreamService.ServiceOptions>>().Clear();

        Assert.Equal(ResolvedBase, monitor.CurrentValue.BaseAddress);
    }

    /// <summary>A settings change rebinds these options. Post-configure has to reapply the address
    /// or the next rebind silently puts the screens back on the dead port.</summary>
    [Fact]
    public void MediaStreamBaseAddress_SurvivesALaterRebind()
    {
        using var provider = Container();
        var monitor = provider.GetRequiredService<IOptionsMonitor<HlsMediaStreamService.ServiceOptions>>();
        var cache = provider.GetRequiredService<IOptionsMonitorCache<HlsMediaStreamService.ServiceOptions>>();

        provider.GetRequiredService<ResolvedHostAddress>().MediaStreamBaseAddress = ResolvedBase;
        cache.Clear();
        Assert.Equal(ResolvedBase, monitor.CurrentValue.BaseAddress);

        cache.Clear();

        Assert.Equal(ResolvedBase, monitor.CurrentValue.BaseAddress);
    }

    /// <summary>An explicit setting always wins, which the resolution spells as leaving the
    /// resolved value null rather than by checking configuration a second time here.</summary>
    [Fact]
    public void MediaStreamBaseAddress_WhenConfigurationNamesOne_IsLeftAlone()
    {
        using var provider = Container(("MediaStream:BaseAddress", "http://khost.local:9000"));

        Assert.Equal(
            "http://khost.local:9000",
            provider.GetRequiredService<IOptionsMonitor<HlsMediaStreamService.ServiceOptions>>()
                .CurrentValue.BaseAddress);
    }

    /// <summary>The screen IPC URI is resolved the same way and reads through <c>IOptions</c>, so
    /// it has to keep working through the same post-configure.</summary>
    [Fact]
    public void ScreenIpcUri_WhenResolvedBeforeTheFirstRead_ReachesTheOptions()
    {
        using var provider = Container();

        provider.GetRequiredService<ResolvedHostAddress>().ScreenIpcUri = ResolvedIpc;

        Assert.Equal(
            ResolvedIpc,
            provider.GetRequiredService<IOptions<LocalScreenProvider.ServiceOptions>>().Value.ServerUri);
    }

    [Fact]
    public void ScreenIpcUri_WhenResolvedBeforeTheFirstRead_ReachesTheMonitor()
    {
        using var provider = Container();

        provider.GetRequiredService<ResolvedHostAddress>().ScreenIpcUri = ResolvedIpc;

        Assert.Equal(
            ResolvedIpc,
            provider.GetRequiredService<IOptionsMonitor<LocalScreenProvider.ServiceOptions>>()
                .CurrentValue.ServerUri);
    }

    /// <summary>Built through the host's own <c>AddDomain()</c>: a hand-wired post-configure here
    /// would keep passing after the real registration was lost.</summary>
    private static ServiceProvider Container(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddDomain();

        return services.BuildServiceProvider();
    }
}
