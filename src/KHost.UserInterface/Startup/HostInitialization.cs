using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Serilog;

namespace KHost.UserInterface.Startup;

/// <summary>Everything that has to run once the container exists and before the first request is
/// served: logging what the plugin loader found, then bringing up the database, the venue-scoped
/// services, plugins and the screen-coordination services in the order the room depends on.</summary>
internal static class HostInitialization
{
    /// <summary>Brings the host up to the point it can start accepting requests.</summary>
    /// <remarks>Called before the endpoints are mapped: a screen able to connect before the
    /// coordination services exist would find nobody listening.</remarks>
    internal static void InitializeHost(this WebApplication app)
    {
        LogDiscoveredPlugins(app.Services.GetRequiredService<IPluginRegistry>());

        // A step the room cannot run without: logged, flushed and rethrown so the process exits
        // rather than serving a console over a half-initialized host.
        void InitializeOrExit(string what, Func<Task> initialize)
        {
            try
            {
                initialize().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "{What} failed", what);
                Log.CloseAndFlush();
                throw;
            }
        }

        // A step the room can run without: logged and swallowed so a missing setup degrades
        // rather than blocking startup.
        void InitializeOrWarn(string what, Func<Task> initialize)
        {
            try
            {
                initialize().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "{What} failed", what);
            }
        }

        InitializeOrExit("Database initialization", () =>
        {
            // The scope must outlive the call, not just its Task: returning the Task itself would
            // dispose the scope the moment it's created, ahead of the awaited work running.
            using var scope = app.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
            return Task.CompletedTask;
        });

        // Before the queue: anything venue-scoped is inert until a venue is selected.
        InitializeOrExit("Venue initialization",
            () => app.Services.GetRequiredService<IVenuesService>().InitializeAsync());

        InitializeOrExit("Singer queue initialization",
            () => app.Services.GetRequiredService<ISingerQueueService>().InitializeAsync());

        InitializeOrExit("Theme service initialization",
            () => app.Services.GetRequiredService<IThemeService>().InitializeAsync());

        // Discovery ran before the container existed, so this is the first moment an entry point can
        // be handed services. Never fatal: PluginInitializer marks a plugin that throws.
        app.Services.GetRequiredService<IPluginInitializer>().InitializeAsync().GetAwaiter().GetResult();

        // Before the hub is mapped: a service nobody has resolved cannot hear the first screen arrive.
        try
        {
            // Each of these wires itself to the broker in its constructor, so enumerating is what
            // makes it exist: a loop, because a line each is what kept going missing.
            foreach (var _ in app.Services.GetServices<KHost.Domain.Services.IStartsWithTheHost>())
            {
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Screen coordination initialization failed");
            Log.CloseAndFlush();
            throw;
        }

        CheckForFFmpeg(app.Services);

        // After the plugins, so a provider one of them registered can be the venue's chosen one.
        // Not fatal: a venue with no break music set up is a venue that runs without it.
        InitializeOrWarn("Break music initialization",
            () => app.Services.GetRequiredService<IBreakMusicService>().InitializeAsync());

        InitializeOrWarn("Ad scheduling initialization",
            () => app.Services.GetRequiredService<IAdService>().InitializeAsync());
    }

    /// <summary>Finds ffmpeg and ffprobe in the background, and warns the console when either is
    /// missing, so a host learns before the first song rather than at it.</summary>
    /// <remarks>Not awaited: asking a program for its version can take seconds on a slow machine
    /// with a virus scanner, and nothing about starting up needs the answer. The ffprobe folder is
    /// applied before the check's first await, so a probe that races it still finds the program.</remarks>
    internal static void CheckForFFmpeg(IServiceProvider services)
    {
        var ffmpeg = services.GetRequiredService<IFFmpegService>();
        var flash = services.GetRequiredService<IFlashService>();

        try
        {
            // Created here so a plugin can rely on the folder existing once the host is up.
            Directory.CreateDirectory(services.GetRequiredService<IHostDirectories>().BinDirectory);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not create the host's bin folder");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var status = await ffmpeg.CheckAsync();

                if (!status.IsReady)
                    flash.Show(FFmpegMissingNotice(status), FlashType.Warning);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Checking for FFmpeg failed");
            }
        });
    }

    internal static string FFmpegMissingNotice(FFmpegStatus status)
    {
        if (status.FFmpeg.IsUsable)
            return "FFprobe could not be found, so imported files will have no length or tags. Install FFmpeg from App Settings.";

        var missing = status.FFprobe.IsUsable ? "FFmpeg" : "FFmpeg and FFprobe";

        return $"{missing} could not be found, so songs will not play. Install FFmpeg from App Settings.";
    }

    /// <summary>Replays what the plugin loader found, once there is somewhere to say it.</summary>
    /// <remarks>The loader runs before the container is built, so it has no logger and records its
    /// outcomes on the plugins themselves. Without this they reach nothing but the Plugins page,
    /// and a plugin that never loaded looks to every log reader like one that was never installed.</remarks>
    internal static void LogDiscoveredPlugins(IPluginRegistry registry)
    {
        var plugins = registry.Plugins;

        if (plugins.Count == 0)
        {
            Log.Information("No plugins discovered");
            return;
        }

        foreach (var plugin in plugins)
        {
            if (plugin.Status is PluginStatus.Errored or PluginStatus.Incompatible)
            {
                Log.Warning("Plugin {Name} ({Id}) in {Directory} is {Status}: {Error}",
                    plugin.DisplayName, plugin.Id, plugin.Directory, plugin.Status, plugin.Error);
            }
            else
            {
                Log.Information("Plugin {Name} ({Id}) is {Status}",
                    plugin.DisplayName, plugin.Id, plugin.Status);
            }

            foreach (var warning in plugin.Warnings)
                Log.Warning("Plugin {Name}: {Warning}", plugin.DisplayName, warning);
        }
    }
}
