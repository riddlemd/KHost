using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Domain.Services.MediaLifetime;
using KHost.IPC.SignalR.Contracts;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using Serilog;

namespace KHost.UserInterface.Startup;

/// <summary>What happens when the host starts serving, and what happens on the way down.</summary>
internal static class HostLifetime
{
    /// <summary>Registers every <see cref="IHostApplicationLifetime"/> hook the host needs.</summary>
    /// <remarks>Must be called before <see cref="NativeShell.RunWithNativeShell"/>: that registers
    /// its own <c>ApplicationStopping</c> hook, and <c>ApplicationStopping</c> callbacks run LIFO,
    /// so the native window's shutdown callback has to be registered last to keep firing first, the
    /// same order today's single method produced.</remarks>
    internal static WebApplication RegisterLifetimeHooks(this WebApplication app)
    {
        // One callback, in this order: ApplicationStarted callbacks run in reverse registration order,
        // so resolving the address after registering the launch ran it first. A screen never retries.
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            ApplyResolvedAddresses(app);
            LaunchStartupScreen(app);
        });

        // Screens we started are ours to close: on macOS closing the window tears the process down
        // inside Photino, so container disposal never runs and a screen would be left announcing a lost host.
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            // Told first: this bypasses LocalScreenDisplayProvider.DisconnectAsync for the reliable
            // close below, so nothing else marks the drop about to happen as one the host asked for.
            foreach (var screen in app.Services.GetServices<IDisplayProvider>().OfType<LocalScreenDisplayProvider>())
                screen.NotifyDisconnectRequested();

            foreach (var provider in app.Services.GetServices<IScreenProvider>())
            {
                try
                {
                    provider.CloseSpawnedScreens();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not close the screens launched by {Provider}", provider.Name);
                }
            }
        });

        // One registration, its steps run in this explicit order: ApplicationStopping fires
        // registrations LIFO, so separate registrations would run reversed. Each step is guarded,
        // so one failing does not skip the rest.
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            // Every graceful exit lands here: Exit menu, close button, or Ctrl+C when headless.
            // Clear-on-close is honoured however KHost quit; swallowed so a stuck queue can't block shutdown.
            try
            {
                app.Services.GetRequiredService<ISingerQueueService>().ClearAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not clear the singer queue while shutting down");
            }

            // A half-written plugin payload is scratch under plugins-staging/.work, which the next
            // install overwrites; cancelling only stops the transfer outliving the host.
            try
            {
                app.Services.GetRequiredService<IPluginInstallerService>().CancelAll();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not cancel in-progress plugin installs while shutting down");
            }

            // A plugin's cleanup may not finish before the process ends. The startup sweep covers
            // whatever it leaves Downloading, but the cancel must fire, or yt-dlp outlives the host.
            try
            {
                app.Services.GetRequiredService<IDownloadsService>().CancelAll();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not cancel in-progress downloads while shutting down");
            }

            // Segments outlive the process, so sweep them on the way down.
            try
            {
                app.Services.GetRequiredService<IMediaStreamService>().CloseAllAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not close active media streams while shutting down");
            }

            // After the streams close, so no encode still holds a file open while it is deleted.
            try
            {
                // After the queue clear above, so only a venue that keeps its queue leaves turns to spare.
                var queued = app.Services.GetRequiredService<IPerformanceService>().ReadQueuedAsync().GetAwaiter().GetResult()
                    .Select(p => p.MediaId).ToHashSet();

                app.Services.GetRequiredService<IMediaLifetimeService>().RemoveFilesOnCloseAsync(queued).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not remove ephemeral or single-use media files while shutting down");
            }
        });

        return app;
    }

    /// <summary>Points launched screens at this host's live listening address.</summary>
    /// <remarks>Works regardless of a dynamic port; an explicit config value always wins.</remarks>
    private static void ApplyResolvedAddresses(WebApplication app)
    {
        var wantsIpcUri = string.IsNullOrWhiteSpace(app.Configuration["LocalScreen:ServerUri"]);
        var wantsStreamAddress = string.IsNullOrWhiteSpace(app.Configuration["MediaStream:BaseAddress"]);

        if (!wantsIpcUri && !wantsStreamAddress)
            return;

        var baseUri = ResolveBaseAddress(app);
        if (baseUri is null)
        {
            Log.Warning("Could not resolve a host listening address; screens will use the configured defaults");
            return;
        }

        var resolved = app.Services.GetRequiredService<ResolvedHostAddress>();

        if (wantsIpcUri)
        {
            var serverUri = $"{baseUri}/ipc/screen";
            resolved.ScreenIpcUri = serverUri;
            ApplyResolved<LocalScreenProvider.ServiceOptions>(app, options => options.ServerUri = serverUri);
            Log.Information("Local screen IPC URI resolved to {ServerUri}", serverUri);
        }

        // A screen fetches HLS from this address, so it has to be the live one.
        if (wantsStreamAddress)
        {
            resolved.MediaStreamBaseAddress = baseUri;
            ApplyResolved<HlsMediaStreamService.ServiceOptions>(app, options => options.BaseAddress = baseUri);
            Log.Information("Media stream base address resolved to {BaseAddress}", baseUri);
        }
    }

    /// <summary>Lands a resolved address on both option caches, which are separate.</summary>
    /// <remarks>IOptions and IOptionsMonitor bind their own instance each, so writing only the
    /// first leaves a monitor reader on the compile-time default — that is how screens were handed
    /// a stream URL on port 5000. Clearing the monitor cache makes the next read rebind and pick
    /// the address up through post-configure; the direct write covers an IOptions instance already
    /// handed to a constructor, which no rebind can reach.</remarks>
    private static void ApplyResolved<TOptions>(WebApplication app, Action<TOptions> set)
        where TOptions : class
    {
        set(app.Services.GetRequiredService<IOptions<TOptions>>().Value);
        app.Services.GetRequiredService<IOptionsMonitorCache<TOptions>>().Clear();
    }

    private static void LaunchStartupScreen(WebApplication app)
    {
        if (!app.Services.GetRequiredService<IAppSettingsService>().Current.LaunchScreenOnStartup)
            return;

        try
        {
            var provider = app.Services.GetServices<IScreenProvider>()
                .FirstOrDefault(candidate => candidate.IsAvailable);

            if (provider is null)
            {
                Log.Warning("A screen was set to launch at startup, but no screen provider is available here");
                return;
            }

            // The same name every night, which is what lets the screen reclaim the window
            // placement it saved last time.
            provider.LaunchAsync(AppSettings.StartupScreenName).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // A console that cannot open a screen is still a console.
            Log.Warning(ex, "Could not launch the startup screen");
        }
    }

    /// <summary>The host's live base address, or null if Kestrel reported none.</summary>
    /// <remarks>Internal: <see cref="NativeShell"/> also opens the window on it.</remarks>
    internal static string? ResolveBaseAddress(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        var httpAddress = addresses?.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? addresses?.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(httpAddress)) return null;

        return httpAddress
            .Replace("://*", "://localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("://[::]", "://localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("://0.0.0.0", "://localhost", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
    }
}
