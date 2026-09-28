using Photino.NET;
using Serilog;

namespace KHost.UserInterface.Startup;

/// <summary>The Photino window the host serves itself into when not running headless.</summary>
internal static class NativeShell
{
    /// <summary>Serves the UI to a Photino window on this thread.</summary>
    /// <remarks>Kestrel keeps listening, so screens and network clients reach the same host.</remarks>
    internal static void RunWithNativeShell(this WebApplication app)
    {
        app.StartAsync().GetAwaiter().GetResult();

        var baseUri = Program.ResolveBaseAddress(app);
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
            .SetAppIcon(app.Logger)
            .SetUseOsDefaultSize(false)
            .SetSize(1440, 900)
            .RegisterWindowCreatedHandler((_, _) => MacDockIcon.TrySet(app.Logger))
            // Blocks both "Inspect Element" in the native text-field menu and F12/Cmd-Opt-I. It is
            // the only switch that closes both, while leaving cut/copy/paste on that menu alone.
            .SetDevToolsEnabled(Program.IsDebugBuild)
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
}
