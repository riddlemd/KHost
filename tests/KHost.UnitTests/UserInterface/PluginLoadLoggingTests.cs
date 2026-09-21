using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.UserInterface;
using NSubstitute;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace KHost.UnitTests.UserInterface;

/// <summary>The plugin loader runs before the container is built, so it has no logger and records
/// its outcomes on the plugins themselves.</summary>
/// <remarks>Nothing replayed them, so an incompatible build, a duplicate id or a failed load
/// reached only the Plugins page: a plugin that never loaded was indistinguishable in the log from
/// one that was never installed, and the only hint was whatever fell back later.</remarks>
public class PluginLoadLoggingTests
{
    [Fact]
    public void LogDiscoveredPlugins_WhenAPluginFailedToLoad_WarnsWithTheReason()
    {
        var events = Capture(Registry(Plugin("Broken", PluginStatus.Errored, "Entry assembly 'x.dll' was not found.")));

        var warning = Assert.Single(events, e => e.Level == LogEventLevel.Warning);

        Assert.Contains("Broken", Rendered(warning));
        Assert.Contains("Entry assembly 'x.dll' was not found.", Rendered(warning));
    }

    /// <summary>The case found on Windows: a stale copy claimed the id and the good build was
    /// rejected, with nothing in the log to say either had happened.</summary>
    [Fact]
    public void LogDiscoveredPlugins_WhenAPluginIsIncompatible_WarnsWithTheReason()
    {
        var events = Capture(Registry(
            Plugin("Spotify Break Music", PluginStatus.Incompatible, "Requires plugin API v1; this host supports v3.")));

        var warning = Assert.Single(events, e => e.Level == LogEventLevel.Warning);

        Assert.Contains("Requires plugin API v1", Rendered(warning));
    }

    [Fact]
    public void LogDiscoveredPlugins_WhenAPluginLoaded_SaysSoWithoutWarning()
    {
        var events = Capture(Registry(Plugin("YouTube Search", PluginStatus.Loaded)));

        var entry = Assert.Single(events);

        Assert.Equal(LogEventLevel.Information, entry.Level);
        Assert.Contains("YouTube Search", Rendered(entry));
        Assert.Contains("Loaded", Rendered(entry));
    }

    /// <summary>Disabled is a host's own choice, not a failure, so it must not read as one.</summary>
    [Fact]
    public void LogDiscoveredPlugins_WhenAPluginIsDisabled_DoesNotWarn()
    {
        var events = Capture(Registry(Plugin("KaraFun Integration", PluginStatus.Disabled)));

        Assert.Equal(LogEventLevel.Information, Assert.Single(events).Level);
    }

    [Fact]
    public void LogDiscoveredPlugins_ReportsEveryPlugin()
    {
        var events = Capture(Registry(
            Plugin("One", PluginStatus.Loaded),
            Plugin("Two", PluginStatus.Disabled),
            Plugin("Three", PluginStatus.Errored, "boom")));

        Assert.Equal(3, events.Count);
    }

    [Fact]
    public void LogDiscoveredPlugins_CarriesEachWarningThePluginCollected()
    {
        var plugin = Plugin("Noisy", PluginStatus.Loaded);
        plugin.Warnings.Add("Icon could not be read.");

        var events = Capture(Registry(plugin));

        Assert.Contains(events, e => e.Level == LogEventLevel.Warning
                                  && Rendered(e).Contains("Icon could not be read."));
    }

    [Fact]
    public void LogDiscoveredPlugins_WhenNoneWereFound_SaysThatRatherThanNothing()
    {
        var events = Capture(Registry());

        Assert.Contains(events, e => Rendered(e).Contains("No plugins discovered"));
    }

    private static DiscoveredPlugin Plugin(string name, PluginStatus status, string? error = null) => new()
    {
        Directory = Path.Combine("plugins", name),
        Manifest = new PluginManifest
        {
            Id = Guid.NewGuid(),
            Name = name,
            Version = "1.0.0",
            EntryAssembly = $"{name}.dll",
            ApiVersion = PluginApi.CurrentVersion,
        },
        Status = status,
        Error = error,
    };

    private static IPluginRegistry Registry(params DiscoveredPlugin[] plugins)
    {
        var registry = Substitute.For<IPluginRegistry>();
        registry.Plugins.Returns(plugins);
        return registry;
    }

    private static string Rendered(LogEvent entry) => entry.RenderMessage();

    /// <summary>Swaps the static logger the host writes through, and puts it back: leaving a test
    /// sink installed would silently swallow every later test's logging.</summary>
    private static List<LogEvent> Capture(IPluginRegistry registry)
    {
        var sink = new ListSink();
        var previous = Log.Logger;

        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

        try
        {
            Program.LogDiscoveredPlugins(registry);
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = previous;
        }

        return sink.Events;
    }

    private sealed class ListSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
