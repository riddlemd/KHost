using FFMpegCore;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using KHost.Abstractions.Repositories;
using KHost.Domain.Services;
using KHost.Domain.Services.PasswordHashers;
using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.DataAccess;
using KHost.Domain;
using KHost.IPC.SignalR;
using KHost.ServiceDefaults;
using KHost.Telemetry;
using KHost.UserInterface.Components;
using KHost.UserInterface.Endpoints;
using KHost.UserInterface.Interactions;
using KHost.UserInterface.Middleware;
using KHost.UserInterface.Interactions.Handlers;
using KHost.UserInterface.Services;
using KHost.UserInterface.Auth;
using KHost.UserInterface.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Photino.NET;
using Serilog;
using Serilog.Events;
using KHost.UserInterface.Services.RedirectProviders;
using KHost.Domain.Services.Screens;

namespace KHost.UserInterface;

internal static class Program
{
    /// <summary>Skips the native shell and runs as a plain web host, for browser-based development.</summary>
    private const string HeadlessFlag = "--headless";
    internal const string NativeShellKey = "NativeShell";

    // The shell locks down by build configuration, not environment: ASPNETCORE_ENVIRONMENT must
    // stay Development for an unpublished run to serve its static assets at all.
#if DEBUG
    internal const bool IsDebugBuild = true;
#else
    internal const bool IsDebugBuild = false;
#endif

    /// <summary>Prints a freshly generated password for the named user, then exits.</summary>
    private const string ResetPasswordFlag = "--reset-password";

    private const string InstanceLockFileName = ".instance.lock";

    internal const string LastLoginCacheKey = "last-login";

    private const int AlreadyRunningExitCode = 1;

    // Top-level statements cannot carry [STAThread], which Photino needs on Windows, and the
    // attribute only holds on a synchronous Main: an async one resumes off the STA thread.
    [STAThread]
    private static int Main(string[] args)
    {
        var headless = args.Contains(HeadlessFlag);
        var resetIndex = Array.IndexOf(args, ResetPasswordFlag);

        using var instanceLock = AcquireInstanceLock();
        if (instanceLock is null)
        {
            // A reset run is a terminal operation, since a native dialog would block a script forever.
            ReportAlreadyRunning(headless || resetIndex >= 0);
            return AlreadyRunningExitCode;
        }

        if (resetIndex >= 0)
            return ResetPassword(resetIndex + 1 < args.Length ? args[resetIndex + 1] : null);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // The command-line config provider rejects a valueless switch, so our own flags never reach it.
            Args = args.Where(a => a != HeadlessFlag).ToArray(),

            // Content root defaults to the working directory, which a desktop launcher sets to
            // anywhere, leaving WebRootPath null and ThemeService dead on startup.
            ContentRootPath = AppContext.BaseDirectory,
        });

        // The window locks itself down (no reload, no back, no inspector); a browser tab does not.
        builder.Configuration.AddInMemoryCollection(
            [new KeyValuePair<string, string?>(NativeShellKey, (!headless).ToString())]);

        // The App Settings page writes this overlay; registered last, it wins over the
        // deployment defaults, and reload-on-change lets IOptionsMonitor bindings apply live.
        builder.Configuration.AddJsonFile(
            Path.Combine(AppContext.BaseDirectory, "cache", AppSettingsService.OverlayFileName),
            optional: true,
            reloadOnChange: true);

        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        foreach (var staleLog in new DirectoryInfo(logDirectory).GetFiles("*.log")
            .Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-7)))
        {
            staleLog.Delete();
        }

        builder.Host.UseSerilog((_, _, cfg) => cfg
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File(
                path: Path.Combine(logDirectory, ".log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: null,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

        builder.AddServiceDefaults();

        builder.Services.AddTelemetry();
        builder.Services.AddDomain();
        builder.Services.AddPlugins();
        builder.Services.AddDataAccess();
        builder.Services.AddSignalRIPCServer();

        var ffmpegPath = builder.Configuration["FFmpegPath"];
        if (!string.IsNullOrWhiteSpace(ffmpegPath))
            GlobalFFOptions.Configure(opts => opts.BinaryFolder = ffmpegPath);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "khost.auth";
                options.LoginPath = "/login";
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
            });

        // One policy per permission, admins passing all of them, so a page can gate itself with
        // [Authorize(Policy = nameof(KHostPermission.X))] and no gate needs its own logic.
        var authorization = builder.Services.AddAuthorizationBuilder();
        foreach (var permission in Enum.GetValues<KHostPermission>())
        {
            authorization.AddPolicy(permission.ToString(), policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole(KHostClaimsFactory.AdminRole)
                    || context.User.HasClaim(KHostClaimsFactory.PermissionClaim, permission.ToString())));
        }

        builder.Services.AddCascadingAuthenticationState();

        builder.Services.AddScoped<IPermissionService, PermissionService>();
        // Scoped, not singleton: a control's pick belongs to the circuit that made it, and a
        // reconnecting browser is a new session rather than one resuming yesterday's choices.
        builder.Services.AddScoped<IControlState, ControlState>();
        builder.Services.AddSingleton<IAppSettingsService>(sp => new AppSettingsService(
            sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IUsersService>()));
        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<IAppInfoService, AppInfoService>();
        builder.Services.AddSingleton<IExternalLinkService, ExternalLinkService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IStartupRedirectProvider, SetupRedirectProvider>();
        builder.Services.AddSingleton<IStartupRedirectProvider, CliStartupRedirectProvider>();

        builder.Services.AddSingleton<IInteractionDispatcher, DialogInteractionDispatcher>();
        builder.Services.AddSingleton<IInteractionHandler<EditMediaRequest, Media?>, EditMediaDialogHandler>();
        builder.Services.AddSingleton<IInteractionHandler<ShowLyricsRequest>, ShowLyricsDialogHandler>();
        builder.Services.AddSingleton<IInteractionHandler<ShowPluginTableRequest>, ShowPluginTableDialogHandler>();
        builder.Services.AddSingleton<IInteractionHandler<ConfirmDuplicateSongRequest, bool>, ConfirmDuplicateSongHandler>();
        builder.Services.AddSingleton<IInteractionHandler<TextPromptRequest, IReadOnlyDictionary<string, string>?>, TextPromptDialogHandler>();

        var app = builder.Build();

        LogDiscoveredPlugins(app.Services.GetRequiredService<IPluginRegistry>());

        try
        {
            using var scope = app.Services.CreateScope();
            var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            initializer.InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Database initialization failed");
            Log.CloseAndFlush();
            throw;
        }

        // Before the queue: anything venue-scoped is inert until a venue is selected.
        try
        {
            app.Services.GetRequiredService<IVenuesService>().InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Venue initialization failed");
            Log.CloseAndFlush();
            throw;
        }

        try
        {
            app.Services.GetRequiredService<ISingerQueueService>().InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Singer queue initialization failed");
            Log.CloseAndFlush();
            throw;
        }

        try
        {
            app.Services.GetRequiredService<IThemeService>().InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Theme service initialization failed");
            Log.CloseAndFlush();
            throw;
        }

        // Discovery ran before the container existed, so this is the first moment an entry point can
        // be handed services. Never fatal: PluginInitializer marks a plugin that throws.
        app.Services.GetRequiredService<IPluginInitializer>().InitializeAsync().GetAwaiter().GetResult();

        // Before the hub is mapped: a service nobody has resolved cannot mute the first screen.
        try
        {
            app.Services.GetRequiredService<IScreenCoordinationService>().InitializeAsync().GetAwaiter().GetResult();
            app.Services.GetRequiredService<IScreenMarqueeService>().InitializeAsync().GetAwaiter().GetResult();
            app.Services.GetRequiredService<BreakMusicCardService>().InitializeAsync().GetAwaiter().GetResult();

            // Each of these wires itself to the broker in its constructor, so enumerating is what
            // makes it exist: a loop, because a line each is what kept going missing.
            foreach (var _ in app.Services.GetServices<KHost.Domain.Services.Screens.IStartsWithTheHost>())
            {
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Screen coordination initialization failed");
            Log.CloseAndFlush();
            throw;
        }

        // After the plugins, so a provider one of them registered can be the venue's chosen one.
        try
        {
            app.Services.GetRequiredService<IBreakMusicService>().InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Not fatal: a venue with no break music set up is a venue that runs without it.
            Log.Warning(ex, "Break music initialization failed");
        }

        try
        {
            app.Services.GetRequiredService<IAdService>().InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Ad scheduling initialization failed");
        }

        app.MapDefaultEndpoints();
        app.MapIPCServer();
        // Native form posts, not circuit calls: a cookie can only be issued on an HTTP response.
        // Antiforgery is off: the console answers loopback only, so a forged post logs someone in or out.
        app.MapPost("/auth/login", async (
            HttpContext http,
            [FromForm] string username,
            [FromForm] string password,
            IAuthService authService,
            IUsersService usersService,
            ICacheService cacheService) =>
        {
            var result = await authService.LoginAsync(username, password);

            if (result is not { Success: true, User: { } user })
                return Results.Redirect("/login?failed=1");

            // Re-read for the groups: the login lookup returns the bare row, and the
            // principal's role and permission claims come from group membership.
            var withGroups = await usersService.ReadAsync(user.Id) ?? user;

            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                KHostClaimsFactory.Create(withGroups, CookieAuthenticationDefaults.AuthenticationScheme));

            // The canonical name, not the typed casing: the lock screen shows who was at the
            // controls, the way an OS lock screen would.
            await cacheService.SaveAsync(LastLoginCacheKey, withGroups.Name);

            return Results.Redirect("/");
        }).AllowAnonymous().DisableAntiforgery();

        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).DisableAntiforgery();

        app.MapMediaStream();
        app.MapMediaImages();
        app.MapBackgroundStills();
        app.MapThemeStylesheets();
        app.MapPluginIcons();

        // One callback, in this order: ApplicationStarted callbacks run in reverse registration order,
        // so resolving the address after registering the launch ran it first. A screen never retries.
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            ApplyResolvedAddresses(app);
            LaunchStartupScreen(app);
        });

        // Segments outlive the process, so sweep them on the way down.
        app.Lifetime.ApplicationStopping.Register(() =>
            app.Services.GetRequiredService<IMediaStreamService>().CloseAllAsync().GetAwaiter().GetResult());

        // A plugin's cleanup may not finish before the process ends. The startup sweep covers
        // whatever it leaves Downloading, but the cancel must fire, or yt-dlp outlives the host.
        app.Lifetime.ApplicationStopping.Register(() =>
            app.Services.GetRequiredService<IDownloadsService>().CancelAll());

        // A half-written plugin payload is scratch under plugins-staging/.work, which the next
        // install overwrites; cancelling only stops the transfer outliving the host.
        app.Lifetime.ApplicationStopping.Register(() =>
            app.Services.GetRequiredService<IPluginInstallerService>().CancelAll());

        // Screens we started are ours to close: on macOS closing the window tears the process down
        // inside Photino, so container disposal never runs and a screen would be left announcing a lost host.
        app.Lifetime.ApplicationStopping.Register(() =>
        {
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

        // Every graceful exit lands here: Exit menu, close button, or Ctrl+C when headless.
        // Clear-on-close is honoured however KHost quit; swallowed so a stuck queue can't block shutdown.
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                app.Services.GetRequiredService<ISingerQueueService>().ClearAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not clear the singer queue while shutting down");
            }
        });

        // Ahead of everything else, including static files: an off-box request must not reach the
        // UI, its assets, or its error pages.
        app.Use(async (context, next) =>
        {
            if (!LanAccessPolicy.IsAllowed(context.Connection.RemoteIpAddress, context.Request.Host, context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next();
        });

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAuthentication();

        // Login requirement off: every session is the console admin, and the gates stay wired and all pass
        // rather than a second code path. Read per request, so the toggle applies on the next page load.
        app.Use((context, next) =>
        {
            if (!(app.Configuration.GetValue<bool?>("Auth:RequireLogin") ?? true))
                context.User = KHostClaimsFactory.CreateConsolePrincipal();

            return next(context);
        });

        app.UseAuthorization();
        app.UseAntiforgery();

        app.UseStartupRedirect();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        if (headless)
        {
            app.Run();
            return 0;
        }

        RunWithNativeShell(app);
        return 0;
    }

    private static int ResetPassword(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine($"Usage: KHost.UserInterface {ResetPasswordFlag} <username>");
            return 1;
        }

        var services = new ServiceCollection()
            .AddLogging()
            .AddDataAccess()
            .AddSingleton<IPasswordHasher, Argon2PasswordHasher>()
            .BuildServiceProvider();

        var exitCode = PasswordReset.RunAsync(
            name,
            services.GetRequiredService<IUsersRepository>(),
            services.GetRequiredService<IPasswordHasher>(),
            Console.Out).GetAwaiter().GetResult();

        if (exitCode == 0)
        {
            // The reset must not be silent: whoever reads the logs sees recovery was used.
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [WRN] Password reset via {ResetPasswordFlag} for '{name}'";
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "logs", $"{DateTime.Now:yyyyMMdd}.log"),
                line + Environment.NewLine);
        }

        return exitCode;
    }

    /// <summary>Tells the user why this launch is stopping.</summary>
    /// <remarks>A shell launch has no console to read, so it gets a native dialog instead.</remarks>
    private static void ReportAlreadyRunning(bool headless)
    {
        const string Message = "Only one instance of KHost can run at a time.";

        if (headless)
        {
            Console.Error.WriteLine(Message);
            return;
        }

        // ShowMessage crashes on a window the native layer has not built yet, so the dialog has to
        // be raised from inside the created handler, which is why a throwaway window hosts it.
        PhotinoWindow? window = null;
        window = new PhotinoWindow()
            .SetTitle("KHost")
            .SetUseOsDefaultSize(false)
            .SetSize(1, 1)
            .RegisterWindowCreatedHandler((_, _) =>
            {
                window!.ShowMessage("KHost", Message, PhotinoDialogButtons.Ok, PhotinoDialogIcon.Warning);

                // Close() here does not break out of WaitForClose, which would leave the process
                // pumping an invisible window forever. Showing the dialog is all this process does.
                Environment.Exit(AlreadyRunningExitCode);
            })
            .LoadRawString("<html><body></body></html>");

        window.WaitForClose();
    }

    /// <summary>Holds an exclusive handle on the lock file, or null when another instance has it.</summary>
    /// <remarks>Scoped to the install directory, so separate installs may coexist.</remarks>
    private static FileStream? AcquireInstanceLock()
    {
        try
        {
            // FileShare.None, and the OS drops the handle even on a kill, so the lock cannot go stale.
            return new FileStream(
                Path.Combine(AppContext.BaseDirectory, InstanceLockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Serves the UI to a Photino window on this thread.</summary>
    /// <remarks>Kestrel keeps listening, so screens and network clients reach the same host.</remarks>
    private static void RunWithNativeShell(WebApplication app)
    {
        app.StartAsync().GetAwaiter().GetResult();

        var baseUri = ResolveBaseAddress(app);
        if (baseUri is null)
        {
            Log.Fatal("Could not resolve a listening address to open the window on");
            Log.CloseAndFlush();
            app.StopAsync().GetAwaiter().GetResult();
            return;
        }

        Log.Information("Opening native shell at {BaseUri}", baseUri);

        // Either side can initiate the close; whichever gets there first owns it.
        var closing = 0;

        var window = new PhotinoWindow()
            .SetTitle("KHost")
            .SetUseOsDefaultSize(false)
            .SetSize(1440, 900)
            // Blocks both "Inspect Element" in the native text-field menu and F12/Cmd-Opt-I. It is
            // the only switch that closes both, while leaving cut/copy/paste on that menu alone.
            .SetDevToolsEnabled(IsDebugBuild)
            // On macOS closing the window tears the process down inside Photino, so the code after
            // WaitForClose never runs there. Shutdown must finish before the close is allowed.
            .RegisterWindowClosingHandler((_, _) =>
            {
                if (Interlocked.Exchange(ref closing, 1) == 0)
                    app.StopAsync().GetAwaiter().GetResult();
                return false;
            })
            .Load(baseUri);

        // On Stopping, not Stopped: with no Run/WaitForShutdown here, nothing performs the stop so
        // Stopped never comes, and on macOS closing the window kills the process before it could.
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            if (Interlocked.CompareExchange(ref closing, 1, 0) != 0) return;

            _ = Task.Run(async () =>
            {
                await app.StopAsync();
                window.Invoke(window.Close);
            });
        });

        window.WaitForClose();

        // Unconditional: stopping an already-stopped host is a no-op, and on the paths that get
        // here without one this is the only stop there is.
        app.StopAsync().GetAwaiter().GetResult();

        Log.CloseAndFlush();
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
    private static string? ResolveBaseAddress(WebApplication app)
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
