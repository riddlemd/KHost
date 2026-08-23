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
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.DataAccess;
using KHost.Domain;
using KHost.Cast;
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

namespace KHost.UserInterface;

internal static class Program
{
    /// <summary>Skips the native shell and runs as a plain web host — browser-based development.</summary>
    private const string HeadlessFlag = "--headless";
    internal const string NativeShellKey = "NativeShell";

    // The shell locks down by build configuration, not environment: a Release build is what an
    // operator runs. Environment cannot carry this — ASPNETCORE_ENVIRONMENT must stay Development
    // for an unpublished run to serve its static assets at all, so it is never a dev/operator tell.
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
    // attribute only holds on a synchronous Main — an async one resumes off the STA thread.
    [STAThread]
    private static int Main(string[] args)
    {
        var headless = args.Contains(HeadlessFlag);
        var resetIndex = Array.IndexOf(args, ResetPasswordFlag);

        using var instanceLock = AcquireInstanceLock();
        if (instanceLock is null)
        {
            // A reset run is a terminal operation — a native dialog would block a script forever.
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
            // anywhere — leaving WebRootPath null and ThemeService dead on startup.
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
        builder.Services.AddCast();

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
        builder.Services.AddSingleton<IAppSettingsService>(sp => new AppSettingsService(
            sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IUsersService>()));
        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IStartupRedirectProvider, SetupRedirectProvider>();
        builder.Services.AddSingleton<IStartupRedirectProvider, CliStartupRedirectProvider>();

        builder.Services.AddSingleton<IInteractionDispatcher, DialogInteractionDispatcher>();
        builder.Services.AddSingleton<IInteractionHandler<EditMediaRequest, Media?>, EditMediaDialogHandler>();
        builder.Services.AddSingleton<IInteractionHandler<ShowLyricsRequest>, ShowLyricsDialogHandler>();
        builder.Services.AddSingleton<IInteractionHandler<ConfirmDuplicateSongRequest, bool>, ConfirmDuplicateSongHandler>();

        var app = builder.Build();

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

        // The second half of plugin loading: discovery ran before the container existed, so this is
        // the first moment an entry point can be handed services. Never fatal — PluginInitializer
        // marks a plugin that throws and leaves the rest of the app alone.
        app.Services.GetRequiredService<IPluginInitializer>().InitializeAsync().GetAwaiter().GetResult();

        // Before the hub is mapped: a service nobody has resolved cannot mute the first screen.
        try
        {
            app.Services.GetRequiredService<IScreenCoordinationService>().InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Screen coordination initialization failed");
            Log.CloseAndFlush();
            throw;
        }

        app.MapDefaultEndpoints();
        app.MapIPCServer();
        // Native form posts, not circuit calls: a cookie can only be issued on an HTTP
        // response. Antiforgery is off here deliberately — the console answers loopback only,
        // and forcing a login/logout is the entire extent of what a forged post could do.
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

        // Point launched screen processes at this host's live listening address, so they
        // connect regardless of the (possibly dynamic, e.g. Aspire-assigned) port.
        // An explicit LocalScreen:ServerUri config value always wins.
        if (string.IsNullOrWhiteSpace(app.Configuration["LocalScreen:ServerUri"]))
        {
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                var baseUri = ResolveBaseAddress(app);
                if (baseUri is null)
                {
                    Log.Warning("Could not resolve a host listening address; screens will use the LocalScreen.ServerUri default");
                    return;
                }

                var options = app.Services.GetRequiredService<IOptions<LocalScreenProvider.ServiceOptions>>().Value;
                options.ServerUri = $"{baseUri}/ipc/screen";
                Log.Information("Local screen IPC URI resolved to {ServerUri}", options.ServerUri);
            });
        }

        // A screen fetches HLS from this address, so it has to be the live one.
        if (string.IsNullOrWhiteSpace(app.Configuration["MediaStream:BaseAddress"]))
        {
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                var baseUri = ResolveBaseAddress(app);
                if (baseUri is null)
                {
                    Log.Warning("Could not resolve a host listening address; screens will use the MediaStream.BaseAddress default");
                    return;
                }

                var options = app.Services.GetRequiredService<IOptions<HlsMediaStreamService.ServiceOptions>>().Value;
                options.BaseAddress = baseUri;
                Log.Information("Media stream base address resolved to {BaseAddress}", options.BaseAddress);
            });
        }

        // Segments outlive the process, so sweep them on the way down.
        app.Lifetime.ApplicationStopping.Register(() =>
            app.Services.GetRequiredService<IMediaStreamService>().CloseAllAsync().GetAwaiter().GetResult());

        // A plugin's cleanup may not get to finish before the process ends — the startup sweep
        // covers whatever it leaves Downloading — but the cancel itself must still fire, or a
        // yt-dlp process outlives the host it was downloading for.
        app.Lifetime.ApplicationStopping.Register(() =>
            app.Services.GetRequiredService<IPluginImportCancellation>().CancelAllImports());

        // Screens we started are ours to close, and nothing else does it: on macOS closing the
        // window tears the process down inside Photino, so container disposal never runs and the
        // screen would be left on the display announcing a lost host.
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

        // Every graceful exit lands here — the Exit menu, the window's close button, Ctrl+C when
        // headless — so the venue's clear-on-close setting is honoured however KHost was quit.
        // Swallowed because a queue that will not clear must not also block the shutdown.
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

        // Login requirement off: every session is the console admin. The gates stay wired and
        // all pass — single-operator mode rather than a second code path through the UI. Read
        // per request, not at startup: the overlay reloads live, so the setup wizard's choice
        // and the App Settings toggle apply on the next page load instead of the next launch.
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

    /// <summary>
    /// Tells the user why this launch is stopping. A shell launch has no console to read, so it
    /// gets a native dialog instead.
    /// </summary>
    private static void ReportAlreadyRunning(bool headless)
    {
        const string Message = "Only one instance of KHost can run at a time.";

        if (headless)
        {
            Console.Error.WriteLine(Message);
            return;
        }

        // ShowMessage crashes on a window the native layer has not built yet, so the dialog has to
        // be raised from inside the created handler — hence the throwaway window hosting it.
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

    /// <summary>
    /// Holds an exclusive handle on a lock file, or null when another instance already has it.
    /// Scoped to the install directory because that is what a second instance would collide over —
    /// the SQLite file under <c>cache/</c> and the configured port. Separate installs may coexist.
    /// </summary>
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

    /// <summary>
    /// Serves the UI to a Photino window on this thread. Kestrel keeps listening throughout, so
    /// screens and (later) network clients reach the same host the window is showing.
    /// </summary>
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
            // The page cannot reach the webview's own developer tools: neither the "Inspect
            // Element" item in a text field's native menu nor F12/Cmd-Opt-I, which are handled
            // before the DOM sees them. This is the only switch that closes both, and it leaves
            // the rest of the text-field menu — cut, copy, paste — alone.
            .SetDevToolsEnabled(IsDebugBuild)
            // On macOS closing the window tears the process down inside Photino, so the code
            // after WaitForClose never runs there. Shutdown work has to finish before the close
            // is allowed to proceed.
            .RegisterWindowClosingHandler((_, _) =>
            {
                if (Interlocked.Exchange(ref closing, 1) == 0)
                    app.StopAsync().GetAwaiter().GetResult();
                return false;
            })
            .Load(baseUri);

        // On Stopping, not Stopped: with no Run/WaitForShutdown in this mode, a SIGTERM only
        // signals the stopping token — nothing performs the stop, so Stopped never comes. The
        // stop must FINISH before the window is touched: on macOS closing it kills the process,
        // and these token callbacks run before the other registered shutdown work.
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
