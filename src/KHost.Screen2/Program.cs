using System.Text.Json;
using KHost.IPC.SignalR;
using Microsoft.Extensions.Logging;
using Photino.NET;
using Serilog;
using Serilog.Events;

namespace KHost.Screen2;

internal static class Program
{
    private static ScreenIpcController? _ipc;
    private static WindowPlacementStore? _placement;

    private static bool _isFullScreen;
    private static int _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;

    [STAThread]
    private static void Main(string[] args)
    {
        var serverUri = GetArg(args, "--server-uri") ?? "http://localhost:5000/ipc/screen";
        var screenId = GetArg(args, "--screen-id") ?? Environment.MachineName;
        var authKey = ReadAuthKey(GetArg(args, "--key-file"));

        var logLevel = ParseLogLevel(GetArg(args, "--log-level"));

        using var loggerFactory = LoggerFactory.Create(b => b
            .SetMinimumLevel(logLevel)
            .AddSerilog(CreateSerilog(screenId, logLevel), dispose: true));

        var logger = loggerFactory.CreateLogger("Screen2");

        var player = new StreamMediaPlayer(loggerFactory.CreateLogger<StreamMediaPlayer>());

        _ipc = new ScreenIpcController(
            ProjectExtensions.CreateScreenClient(loggerFactory),
            player,
            loggerFactory.CreateLogger<ScreenIpcController>());

        _placement = new WindowPlacementStore(screenId, AppContext.BaseDirectory, logger);

        // Where this screen was last left. A first run has none, so it opens at a size that fits
        // any monitor rather than filling whatever it landed on.
        var stored = _placement.Read();
        var (left, top) = (stored?.Left ?? 80, stored?.Top ?? 80);
        var (width, height) = (stored?.Width ?? 1280, stored?.Height ?? 720);

        // Seeded so leaving full screen returns to the remembered window, not to 0x0.
        (_restoreLeft, _restoreTop, _restoreWidth, _restoreHeight) = (left, top, width, height);

        logger.LogInformation(
            stored is null
                ? "No stored window placement; opening at {Left},{Top} {Width}x{Height}"
                : "Restoring window placement {Left},{Top} {Width}x{Height} (full screen {FullScreen})",
            left, top, width, height, stored?.FullScreen ?? false);

        PhotinoWindow? window = null;
        var ready = false;
        window = new PhotinoWindow()
            .SetTitle("KHost Screen")
#if !DEBUG
            .SetDevToolsEnabled(false)
#endif
            // Photino logs every SendWebMessage into the host's inherited stdout.
            .SetLogVerbosity(0)
            // Chromeless cannot change after creation and has no title bar to drag, so full
            // screen is faked by resizing instead.
            .SetChromeless(false)
            .SetUseOsDefaultSize(false)
            .SetUseOsDefaultLocation(false)
            .SetSize(width, height)
            .SetLeft(left)
            .SetTop(top)
            // Saved as it moves, not on the way out: the host closes a screen by killing the
            // process, so an exit handler would never run on the ordinary path.
            .RegisterLocationChangedHandler((_, _) => Remember(window!))
            .RegisterSizeChangedHandler((_, _) => Remember(window!))
            .RegisterMaximizedHandler((_, _) => Remember(window!))
            .RegisterRestoredHandler((_, _) => Remember(window!))
            .RegisterWebMessageReceivedHandler((_, message) =>
            {
                if (message is null) return;

                // The page saying it is wired up. Registering before this lets the host answer
                // with a command, and SendWebMessage into a web view with no page yet is a native
                // crash that takes the screen down with no managed exception to log.
                if (message.Contains("\"ready\"", StringComparison.Ordinal) && !ready)
                {
                    ready = true;

                    player.SendToBrowser = json => window!.SendWebMessage(json);

                    _ = ConnectAsync(logger, serverUri, screenId, authKey);
                    _ = PublishStateAsync();
                    _ = ResyncClockAsync();

                    // Applied here rather than at construction: full screen resizes the window,
                    // which needs one that exists. The page saying it is ready is the first
                    // moment that is true.
                    if (stored?.FullScreen == true)
                        SetFullScreen(window!, true, logger);

                    return;
                }

                if (!player.HandleBrowserMessage(message)) HandleWindowMessage(window!, message, logger);
            })
            // Handed to the web view as a string: this screen serves nothing and has no files
            // on disk, the media comes from the host.
            .LoadRawString(BuildPlayerPage());

        logger.LogInformation("Screen2 starting: server={ServerUri} screen={ScreenId}", serverUri, screenId);

        window.WaitForClose();

        _placement.Dispose();
        _ipc.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Log.CloseAndFlush();
    }

    /// <summary>Builds the player page with its script inlined, from the embedded resources.</summary>
    internal static string BuildPlayerPage()
    {
        var html = ReadResource("screen-ui/index.html");

        // hls.js first: player.js reads Hls at load time to pick its playback path.
        html = Inline(html, "hls.light.min.js");
        html = Inline(html, "player.js");

        return html;
    }

    /// <summary>
    /// Replaces a script tag with the script itself. Without the guard, renaming a tag in
    /// index.html yields a page missing that script: a black window, no error, and nothing in
    /// the log to say why.
    /// </summary>
    private static string Inline(string html, string fileName)
    {
        var scriptTag = $"<script src=\"{fileName}\"></script>";
        if (!html.Contains(scriptTag, StringComparison.Ordinal))
            throw new InvalidOperationException($"index.html no longer contains {scriptTag} for {fileName} to be inlined into.");

        return html.Replace(scriptTag, $"<script>{ReadResource($"screen-ui/{fileName}")}</script>", StringComparison.Ordinal);
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' is missing.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Records where the window is now. Full screen is remembered as a flag rather than as the
    /// monitor's own bounds: the screen may come back on a different monitor, and restoring
    /// yesterday's pixels there would leave it part-way off the picture.
    /// </summary>
    private static void Remember(PhotinoWindow window)
    {
        if (_placement is null) return;

        try
        {
            _placement.Schedule(_isFullScreen
                ? new WindowPlacement(_restoreLeft, _restoreTop, _restoreWidth, _restoreHeight, true)
                : new WindowPlacement(window.Left, window.Top, window.Width, window.Height, false));
        }
        catch
        {
            // Reading the window can throw while it is being torn down; a lost position is nothing.
        }
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
    /// Photino's SetFullScreen does nothing on macOS, so the window is grown to cover the monitor.
    /// macOS clamps the top edge below the menu bar.
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

                // Not SetTopMost: a floating window on macOS never becomes key, so Escape stops
                // reaching the page.
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
            Remember(window);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not change the window to full screen {FullScreen}", fullScreen);
        }
    }

    private static async Task ConnectAsync(Microsoft.Extensions.Logging.ILogger logger, string serverUri, string screenId, byte[] authKey)
    {
        try
        {
            await _ipc!.ConnectAsync(serverUri, screenId, authKey);
            logger.LogInformation("Connected to IPC server");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to IPC server at {Uri}", serverUri);
        }
    }

    /// <summary>The host writes this file and hands its path in; its absence means an unprovisioned launch.</summary>
    private static byte[] ReadAuthKey(string? keyFilePath)
    {
        if (string.IsNullOrWhiteSpace(keyFilePath))
            throw new InvalidOperationException("No --key-file was given; the host provisions one for every screen it launches.");

        return Convert.FromBase64String(File.ReadAllText(keyFilePath).Trim());
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

    /// <summary>
    /// Anchored to the executable, not the working directory: a screen the host launches inherits
    /// whatever directory the host happened to be in, and its log would land somewhere nobody looks.
    /// The screen id is in the name because a venue runs several at once.
    /// </summary>
    private static Serilog.Core.Logger CreateSerilog(string screenId, LogLevel minimum)
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        foreach (var staleLog in new DirectoryInfo(logDirectory).GetFiles("*.log")
            .Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-7)))
        {
            try { staleLog.Delete(); } catch (IOException) { /* another screen still holds it */ }
        }

        var safeId = string.Join("_", screenId.Split(Path.GetInvalidFileNameChars()));

        return new LoggerConfiguration()
            .MinimumLevel.Is(ToSerilog(minimum))
            .WriteTo.Console()
            .WriteTo.File(
                path: Path.Combine(logDirectory, $"{safeId}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: null,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    /// <summary>
    /// Debug carries the raw state the page reports each tick, which is the only way to see what a
    /// screen thinks its playhead is doing.
    /// </summary>
    private static LogLevel ParseLogLevel(string? value)
        => Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level) ? level : LogLevel.Information;

    private static LogEventLevel ToSerilog(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };

    private static string? GetArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

}
