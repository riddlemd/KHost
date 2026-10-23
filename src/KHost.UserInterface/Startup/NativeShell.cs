using KHost.UserInterface.Components;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Web;
using Oriel;
using Oriel.Blazor;
using Serilog;

namespace KHost.UserInterface.Startup;

/// <summary>The console window: the UI's components rendered in-process into an Oriel window (Blazor Hybrid).</summary>
/// <remarks>Kestrel keeps serving beside it, for screens, network clients and the media the page loads.</remarks>
internal static class NativeShell
{
    private const int Width = 1440;
    private const int Height = 900;

    /// <summary>Starts the host, opens the window on this thread, and stops the host once the window has closed.</summary>
    internal static int RunWithNativeShell(this WebApplication app, OrielApp shell)
    {
        app.StartAsync().GetAwaiter().GetResult();

        var baseUri = HostLifetime.ResolveBaseAddress(app);
        if (baseUri is null)
        {
            Log.Fatal("Could not resolve a listening address for the window's media");
            Log.CloseAndFlush();
            app.StopAsync().GetAwaiter().GetResult();
            return 1;
        }

        // The page's own media and theme requests, on the app:// origin, go on to the host here.
        using var host = new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(baseUri + "/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var lockDown = !Program.IsDebugBuild;
        var startPath = StartPathAsync(app).GetAwaiter().GetResult();

        BrowserWindow? window = null;
        shell.Ready += (_, _) =>
        {
            MacDockIcon.TrySet(app.Logger);

            window = new BrowserWindow(new BrowserWindowOptions
            {
                Title = "KHost",
                Width = Width,
                Height = Height,
                Icon = AppIcon.ResolveWindowIcon(AppContext.BaseDirectory, AppIcon.CurrentPlatform),
                BackgroundColor = "#140a24",
                // Release closes the inspector and its shortcuts; the page's own lockdown is shell.js.
                WebPreferences = new WebPreferences { DevTools = !lockDown },
            });

            window.PageTitleUpdated += KeepAppTitle;

            // A link to the outside opens the browser, never a second app window.
            var links = app.Services.GetRequiredService<IExternalLinkService>();
            window.WebContents.SetWindowOpenHandler(details =>
            {
                links.Open(details.Url);
                return WindowOpenHandlerResponse.Deny;
            });

            window.LoadBlazor<Routes>(app.Services, options: new BlazorWebViewOptions
            {
                HostPage = "native.html",
                StartPath = startPath,
                RootComponents = [new BlazorRootComponent(typeof(HeadOutlet), "head::after")],
                RequestHandler = (request, ct) => NativeShellRequests.HandleAsync(request, host, lockDown, ct),
            });

            Log.Information("Opened the console window at {StartPath}; media from {BaseUri}", startPath, baseUri);
        };

        // The host can stop on its own (a restart after an update); the window goes with it.
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            // Closing the window stops Chromium first and the host after; then there is nothing left to quit.
            try { shell.Quit(); }
            catch (InvalidOperationException) { }
        });

        // Returns once the last window has closed.
        var exitCode = shell.Run();

        // Unconditional: stopping an already-stopped host is a no-op.
        app.StopAsync().GetAwaiter().GetResult();
        Log.CloseAndFlush();
        return exitCode;
    }

    /// <summary>Keeps the window titled "KHost".</summary>
    /// <remarks>Each page titles itself ("KHost: Hosting Dashboard") and Oriel, as Electron, would put that on the
    /// title bar and taskbar.</remarks>
    internal static void KeepAppTitle(object? sender, PageTitleUpdatedEventArgs e) => e.Cancel = true;

    /// <summary>Where the window opens: what a browser tab's first request would be redirected to (the setup wizard
    /// on a new install, or a <c>--start-page</c>), else the console.</summary>
    internal static async Task<string> StartPathAsync(WebApplication app)
    {
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Path = "/";
        foreach (var provider in app.Services.GetServices<IStartupRedirectProvider>())
        {
            if (await provider.ShouldRedirectAsync(context)) return await provider.GetRedirectPathAsync();
        }
        return "/";
    }
}
