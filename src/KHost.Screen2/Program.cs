using System.Text.Json;
using KHost.IPC.SignalR;
using Microsoft.Extensions.Logging;
using Photino.NET;
using Serilog;

namespace KHost.Screen2;

internal static class Program
{
    private static ScreenIpcController? _ipc;

    private static bool _isFullScreen;
    private static int _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;

    [STAThread]
    private static void Main(string[] args)
    {
        var logPath = $"logs/{DateTime.Now:yyyyMMddHHmmss}.Screen2.log";
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath, outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        using var loggerFactory = LoggerFactory.Create(b => b
            .SetMinimumLevel(LogLevel.Information)
            .AddSerilog(serilog, dispose: true));

        var logger = loggerFactory.CreateLogger("Screen2");

        var serverUri = GetArg(args, "--server-uri") ?? "http://localhost:5000/ipc/screen";
        var screenId = GetArg(args, "--screen-id") ?? Environment.MachineName;

        var player = new StreamMediaPlayer(loggerFactory.CreateLogger<StreamMediaPlayer>());

        _ipc = new ScreenIpcController(
            ProjectExtensions.CreateScreenClient(loggerFactory),
            player,
            loggerFactory.CreateLogger<ScreenIpcController>());

        PhotinoWindow? window = null;
        window = new PhotinoWindow()
            .SetTitle("KHost Screen")
            // Photino logs every SendWebMessage, and a timeline goes out once a second — into the
            // host's stdout, because a launched screen inherits it.
            .SetLogVerbosity(0)
            // Chromeless cannot be changed after creation, and a chromeless window has no title
            // bar to drag — so the screen keeps its chrome and fakes full screen by resizing.
            .SetChromeless(false)
            .SetUseOsDefaultSize(false)
            .SetUseOsDefaultLocation(false)
            .SetSize(1280, 720)
            .SetLeft(80)
            .SetTop(80)
            .RegisterWebMessageReceivedHandler((_, message) =>
            {
                if (message is null) return;
                if (!player.HandleBrowserMessage(message)) HandleWindowMessage(window!, message, logger);
            })
            .RegisterWindowCreatedHandler((_, _) =>
            {
                player.SendToBrowser = json => window!.SendWebMessage(json);

                _ = ConnectAsync(logger, serverUri, screenId);
                _ = PublishStateAsync();
                _ = ResyncClockAsync();
            })
            // A local file, not a loopback server: the only thing this screen serves now is its
            // own page, and the media it plays comes from the host over the network.
            .Load(new Uri(Path.Combine(AppContext.BaseDirectory, "screen-ui", "index.html")));

        logger.LogInformation("Screen2 starting: server={ServerUri} screen={ScreenId}", serverUri, screenId);

        window.WaitForClose();

        _ipc.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Log.CloseAndFlush();
    }

    /// <summary>Handles the page messages that drive the window rather than the player.</summary>
    private static void HandleWindowMessage(PhotinoWindow window, string message, Microsoft.Extensions.Logging.ILogger logger)
    {
        string? type;
        try
        {
            using var document = JsonDocument.Parse(message);
            type = document.RootElement.TryGetProperty("type", out var p) ? p.GetString() : null;
        }
        catch (JsonException)
        {
            return;
        }

        switch (type)
        {
            case "toggle-fullscreen":
                SetFullScreen(window, !_isFullScreen, logger);
                break;
            case "exit-fullscreen":
                if (_isFullScreen) SetFullScreen(window, false, logger);
                break;
        }
    }

    /// <summary>
    /// Stands in for real fullscreen, which Photino's SetFullScreen does not deliver on macOS:
    /// the window is grown to cover the monitor instead. Note that macOS clamps the top edge
    /// below the menu bar on a display that has one.
    /// </summary>
    private static void SetFullScreen(PhotinoWindow window, bool fullScreen, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            if (fullScreen)
            {
                (_restoreLeft, _restoreTop) = (window.Left, window.Top);
                (_restoreWidth, _restoreHeight) = (window.Width, window.Height);

                var monitors = window.Monitors;
                var area = (monitors.Count > 0 ? monitors[0] : window.MainMonitor).MonitorArea;

                // Deliberately not SetTopMost: a floating window on macOS never becomes key, so
                // the page stops receiving keydown and Escape can no longer leave full screen.
                window.SetLeft(area.X);
                window.SetTop(area.Y);
                window.SetSize(area.Width, area.Height);

                logger.LogInformation("Full screen on monitor {X},{Y} {Width}x{Height}", area.X, area.Y, area.Width, area.Height);
            }
            else
            {
                window.SetSize(_restoreWidth, _restoreHeight);
                window.SetLeft(_restoreLeft);
                window.SetTop(_restoreTop);

                logger.LogInformation("Restored to {X},{Y} {Width}x{Height}", _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight);
            }

            _isFullScreen = fullScreen;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not change the window to full screen {FullScreen}", fullScreen);
        }
    }

    private static async Task ConnectAsync(Microsoft.Extensions.Logging.ILogger logger, string serverUri, string screenId)
    {
        try
        {
            await _ipc!.ConnectAsync(serverUri, screenId);
            logger.LogInformation("Connected to IPC server");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to IPC server at {Uri}", serverUri);
        }
    }

    /// <summary>Keeps the host's position display live; commands alone only report on completion.</summary>
    private static async Task PublishStateAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            if (_ipc is null) return;
            await _ipc.SendCurrentStateAsync();
        }
    }

    /// <summary>Machine clocks drift, and a stale offset biases this screen off the group.</summary>
    private static async Task ResyncClockAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(5));
            if (_ipc is null) return;
            await _ipc.ResyncClockAsync();
        }
    }

    private static string? GetArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

}
