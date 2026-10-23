using KHost.DataAccess;
using KHost.Domain;
using KHost.IPC.SignalR;
using KHost.ServiceDefaults;
using KHost.Telemetry;
using KHost.UserInterface.Endpoints;
using KHost.UserInterface.Middleware;
using KHost.UserInterface.Startup;
using KHost.Common.Storage;
using Oriel;
using Oriel.Blazor;

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

    // The window's thread runs Windows' OLE drag and drop and native dialogs, which need an STA. Top-level
    // statements cannot carry the attribute, and it only holds on a synchronous Main.
    [STAThread]
    private static int Main(string[] args)
    {
        // First: the already-running path below writes to a console that, on Windows, exists only
        // once attached.
        ParentConsole.TryAttach();

        var headless = args.Contains(HeadlessFlag);

        // A build that kept its data beside the executable brings it along, once, to where it lives now.
        if (AppPaths.AdoptLegacyData(AppContext.BaseDirectory, AppPaths.DataRoot))
            Console.WriteLine($"Copied the data beside the executable into {AppPaths.DataRoot}");

        using var instanceLock = InstanceLock.TryAcquire();
        if (instanceLock is null)
        {
            InstanceLock.ReportAlreadyRunning(headless);
            return InstanceLock.AlreadyRunningExitCode;
        }

        // Before anything else touches Chromium: the window's app, and the app:// scheme its page loads from.
        var shell = headless ? null : OrielApp.Create("KHost");
        // Chromium's own profile, kept out of the folder KHost's data shares with it by default.
        shell?.SetPath("userData", Path.Combine(AppPaths.DataRoot, "browser"));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // The command-line config provider rejects a valueless switch, so our own flags never reach it.
            Args = args.Where(a => a != HeadlessFlag).ToArray(),

            // Content root defaults to the working directory, which a desktop launcher sets to
            // anywhere, leaving WebRootPath null and ThemeService dead on startup.
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.AddKHostConfiguration(headless);
        builder.AddKHostLogging();

        builder.AddServiceDefaults();

        builder.Services.AddTelemetry();
        builder.Services.AddDomain();
        builder.Services.AddPlugins();
        builder.Services.AddDataAccess();
        builder.Services.AddSignalRIPCServer();

        // A browser tab renders over a Blazor Server circuit; the window renders in-process. One renderer
        // per run: the two register different JS runtimes and navigation managers.
        if (shell is null)
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();
        else
            builder.Services.AddOrielBlazor();

        builder.Services.AddUserInterfaceServices();
        builder.Services.AddInteractionHandlers();

        var app = builder.Build();

        app.InitializeHost();

        app.MapDefaultEndpoints();
        app.MapIPCServer();
        app.MapMediaStream();
        app.MapMediaImages();
        app.MapSongLevels();
        app.MapVideoBackdrops();
        app.MapVisualiserPresets();
        app.MapThemeStylesheets();
        app.MapPluginIcons();

        app.RegisterLifetimeHooks();

        app.UseKHostPipeline(serveConsole: shell is null);

        if (shell is null)
        {
            app.Run();
            return 0;
        }

        return app.RunWithNativeShell(shell);
    }
}
