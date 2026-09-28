using FFMpegCore;
using KHost.DataAccess;
using KHost.Domain;
using KHost.IPC.SignalR;
using KHost.ServiceDefaults;
using KHost.Telemetry;
using KHost.UserInterface.Endpoints;
using KHost.UserInterface.Middleware;
using KHost.UserInterface.Startup;

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

    internal const string LastLoginCacheKey = "last-login";

    // Top-level statements cannot carry [STAThread], which Photino needs on Windows, and the
    // attribute only holds on a synchronous Main: an async one resumes off the STA thread.
    [STAThread]
    private static int Main(string[] args)
    {
        var headless = args.Contains(HeadlessFlag);
        var resetIndex = Array.IndexOf(args, PasswordResetCommand.Flag);

        using var instanceLock = InstanceLock.TryAcquire();
        if (instanceLock is null)
        {
            // A reset run is a terminal operation, since a native dialog would block a script forever.
            InstanceLock.ReportAlreadyRunning(headless || resetIndex >= 0);
            return InstanceLock.AlreadyRunningExitCode;
        }

        if (resetIndex >= 0)
            return PasswordResetCommand.Run(resetIndex + 1 < args.Length ? args[resetIndex + 1] : null);

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

        var ffmpegPath = builder.Configuration["FFmpegPath"];
        if (!string.IsNullOrWhiteSpace(ffmpegPath))
            GlobalFFOptions.Configure(opts => opts.BinaryFolder = ffmpegPath);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddKHostAuthentication();
        builder.Services.AddUserInterfaceServices();
        builder.Services.AddInteractionHandlers();

        var app = builder.Build();

        app.InitializeHost();

        app.MapDefaultEndpoints();
        app.MapIPCServer();
        app.MapAuthEndpoints();
        app.MapMediaStream();
        app.MapMediaImages();
        app.MapSongLevels();
        app.MapBackgroundStills();
        app.MapThemeStylesheets();
        app.MapPluginIcons();

        app.RegisterLifetimeHooks();

        app.UseKHostPipeline();

        if (headless)
        {
            app.Run();
            return 0;
        }

        app.RunWithNativeShell();
        return 0;
    }
}
