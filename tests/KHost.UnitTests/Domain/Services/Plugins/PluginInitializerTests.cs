using KHost.Abstractions.Models.Plugins;
using KHost.Domain.Services.Plugins;
using KHost.Plugins.Sdk;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginInitializerTests
{
    [Fact]
    public async Task InitializeAsync_RunsEveryEntryPoint()
    {
        var first = new SpyPlugin();
        var second = new SpyPlugin();

        await Initializer(Loaded(first), Loaded(second)).InitializeAsync();

        Assert.True(first.Initialized);
        Assert.True(second.Initialized);
    }

    [Fact]
    public async Task InitializeAsync_APluginThatThrows_IsMarkedAndTheRestStillRun()
    {
        var broken = Loaded(new SpyPlugin { Throw = "no yt-dlp anywhere" });
        var after = new SpyPlugin();

        // Never rethrown: one plugin's bad setup must not be why a venue cannot open the console.
        await Initializer(broken, Loaded(after)).InitializeAsync();

        Assert.Equal(PluginStatus.Errored, broken.Discovered.Status);
        Assert.Contains("no yt-dlp anywhere", broken.Discovered.Error);
        Assert.True(after.Initialized);
    }

    [Fact]
    public async Task InitializeAsync_HandsThePluginItsOwnContext()
    {
        var plugin = new SpyPlugin();
        var loaded = Loaded(plugin);

        await Initializer(loaded).InitializeAsync();

        // The context is the only way back to the host, so the wrong one is silent misreporting.
        plugin.Context!.ReportWarning("installed the slow way");

        Assert.Contains("installed the slow way", loaded.Discovered.Warnings);
    }

    private static PluginInitializer Initializer(params LoadedPlugin[] plugins)
        => new(NullLogger<PluginInitializer>.Instance, plugins);

    private static LoadedPlugin Loaded(IPlugin entryPoint)
    {
        var manifest = new PluginManifest
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Version = "1.0.0",
            EntryAssembly = "Test.dll",
            ApiVersion = PluginApi.CurrentVersion,
        };

        var discovered = new DiscoveredPlugin { Directory = "/plugins/test", Manifest = manifest };

        return new LoadedPlugin(discovered, entryPoint, new PluginContext(manifest, null, discovered));
    }

    private sealed class SpyPlugin : IPlugin
    {
        public bool Initialized { get; private set; }
        public string? Throw { get; init; }
        public IPluginContext? Context { get; private set; }

        public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
        {
            Context = context;
            Initialized = true;

            return Throw is null ? Task.CompletedTask : throw new InvalidOperationException(Throw);
        }
    }
}
