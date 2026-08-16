using FFMpegCore;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using KHost.Domain.Services;
using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess;
using KHost.Domain;
using KHost.IPC.SignalR;
using KHost.ServiceDefaults;
using KHost.Telemetry;
using KHost.UserInterface.Components;
using KHost.UserInterface.Interactions;
using KHost.UserInterface.Middleware;
using KHost.UserInterface.Interactions.Handlers;
using KHost.UserInterface.Services;
using Photino.NET;
using Serilog;
using Serilog.Events;
using KHost.UserInterface.Services.RedirectProviders;

namespace KHost.UserInterface;

internal static class Program
{
    /// <summary>Skips the native shell and runs as a plain web host — browser-based development.</summary>
    private const string HeadlessFlag = "--headless";

    private const string InstanceLockFileName = ".instance.lock";

    // Top-level statements cannot carry [STAThread], which Photino needs on Windows, and the
    // attribute only holds on a synchronous Main — an async one resumes off the STA thread.
    [STAThread]
    private static int Main(string[] args)
    {
        var headless = args.Contains(HeadlessFlag);

        using var instanceLock = AcquireInstanceLock();
        if (instanceLock is null)
        {
            ReportAlreadyRunning(headless);
            return 1;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // The command-line config provider rejects a valueless switch, so our own flags never reach it.
            Args = args.Where(a => a != HeadlessFlag).ToArray(),

            // Content root defaults to the working directory, which a desktop launcher sets to
            // anywhere — leaving WebRootPath null and ThemeService dead on startup.
            ContentRootPath = AppContext.BaseDirectory,
        });

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

        // Configure FFmpeg
        var ffmpegPath = builder.Configuration["FFmpegPath"];
        if (!string.IsNullOrWhiteSpace(ffmpegPath))
            GlobalFFOptions.Configure(opts => opts.BinaryFolder = ffmpegPath);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IStartupRedirectProvider, SetupRedirectProvider>();
        builder.Services.AddSingleton<IStartupRedirectProvider, CliStartupRedirectProvider>();

        builder.Services.AddSingleton<IInteractionDispatcher, DialogInteractionDispatcher>();
        builder.Services.AddSingleton<IInteractionHandler<EditMediaRequest, Media?>, EditMediaDialogHandler>();
        builder.Services.AddSingleton<IInteractionHandler<ShowLyricsRequest>, ShowLyricsDialogHandler>();
        builder.Services.AddSingleton<IInteractionHandler<ConfirmDuplicateSongRequest, bool>, ConfirmDuplicateSongHandler>();

        var app = builder.Build();

        // Initialize database with seed data
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

        app.MapDefaultEndpoints();
        app.MapIPCServer();

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

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
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
                window.Close();
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

        var window = new PhotinoWindow()
            .SetTitle("KHost")
            .SetUseOsDefaultSize(false)
            .SetSize(1440, 900)
            .Load(baseUri);

        // Either side can initiate shutdown; whichever gets there first owns it.
        var shuttingDown = 0;
        app.Lifetime.ApplicationStopped.Register(() =>
        {
            if (Interlocked.Exchange(ref shuttingDown, 1) == 0)
                window.Invoke(window.Close);
        });

        window.WaitForClose();

        if (Interlocked.Exchange(ref shuttingDown, 1) == 0)
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

        // Normalize wildcard hosts (http://*:5251, http://[::]:5251, http://0.0.0.0:5251) to localhost.
        return httpAddress
            .Replace("://*", "://localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("://[::]", "://localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("://0.0.0.0", "://localhost", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
    }
}
