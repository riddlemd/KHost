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
    private static readonly CancellationTokenSource _closing = new();

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

                // The page saying it is wired up. SendWebMessage into a web view with no page yet is
                // a native crash that takes the screen down with no managed exception to log.
                if (message.Contains("\"ready\"", StringComparison.Ordinal) && !ready)
                {
                    ready = true;

                    player.SendToBrowser = json => window!.SendWebMessage(json);

                    _ = ConnectAsync(logger, serverUri, screenId, authKey);
                    _ = PublishStateAsync();
                    _ = ResyncClockAsync();

                    // Before full screen, and here rather than at construction because both need a
                    // window that exists — the page being ready is the first moment that is true.
                    EnsureReachable(window!, logger);

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

        // Stops the connect retry loop, which otherwise keeps a closing screen alive waiting on
        // its next delay.
        _closing.Cancel();

        _placement.Dispose();
        _ipc.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Log.CloseAndFlush();
        _closing.Dispose();
    }

    /// <summary>Photino's own full screen, which takes the window's frame with it.</summary>
    /// <returns>False when this build refuses it after the window exists, so the caller falls back
    /// to filling the monitor and keeping the title bar — worse, but not broken.</returns>
    private static bool TryNativeFullScreen(
        PhotinoWindow window, bool fullScreen, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            window.SetFullScreen(fullScreen);
            logger.LogInformation("Native full screen {FullScreen}", fullScreen);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Native full screen is unavailable; filling the monitor instead");

            return false;
        }
    }

    /// <summary>Puts the window back on a monitor when the one it was left on is not there.</summary>
    /// <remarks>The stored placement can be perfectly sensible and still land nowhere: a venue runs
    /// the screen on a projector, unplugs it, and the next launch restores onto coordinates that no
    /// longer exist. The window then has no title bar on screen to drag and no way back, so this
    /// costs the remembered position rather than the window.</remarks>
    private static void EnsureReachable(PhotinoWindow window, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            var monitors = window.Monitors.ToList();

            // Nothing to measure against: leave the window where it is rather than moving it on a
            // guess.
            if (monitors.Count == 0) return;

            var left = window.Left;
            var top = window.Top;
            var right = left + window.Width;
            var bottom = top + window.Height;

            // Any overlap at all is enough — a window half off the side is still draggable.
            foreach (var monitor in monitors)
            {
                var area = monitor.WorkArea;
                if (left < area.X + area.Width && right > area.X
                    && top < area.Y + area.Height && bottom > area.Y)
                    return;
            }

            var main = window.MainMonitor.WorkArea;
            var x = main.X + Math.Max(0, (main.Width - window.Width) / 2);
            var y = main.Y + Math.Max(0, (main.Height - window.Height) / 2);

            logger.LogWarning(
                "The stored placement {Left},{Top} is on no monitor; centring on the main one at {X},{Y}",
                left, top, x, y);

            window.SetLeft(x);
            window.SetTop(y);
        }
        catch (Exception ex)
        {
            // Never worth failing a screen over: a window in an odd place still shows the song.
            logger.LogWarning(ex, "Could not check the window is on a monitor");
        }
    }

    /// <summary>Builds the player page with its script inlined, from the embedded resources.</summary>
    internal static string BuildPlayerPage()
    {
        var html = ReadResource("screen-ui/index.html");

        // hls.js first: player.js reads Hls at load time to pick its playback path.
        html = Inline(html, "hls.light.min.js");
        // Order here is immaterial: each call swaps a tag for its script where the tag already
        // sits, so the page's own tag order is what decides what is defined first.
        html = Inline(html, "lyrics-overlay.js");
        html = Inline(html, "player.js");

        return html;
    }

    /// <summary>Replaces a script tag with the script itself.</summary>
    /// <remarks>A renamed tag in index.html then throws, instead of showing a silent black window.</remarks>
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

    /// <summary>Full screen is remembered as a flag, not the monitor's own bounds.</summary>
    /// <remarks>A screen may return on a different monitor, where old pixels leave it part-way off.</remarks>
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

    /// <summary>Photino's SetFullScreen is a no-op on macOS, so the window is grown to cover it.</summary>
    /// <remarks>macOS clamps the top edge below the menu bar.</remarks>
    private static void SetFullScreen(PhotinoWindow window, bool fullScreen, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            if (fullScreen)
            {
                (_restoreLeft, _restoreTop) = (window.Left, window.Top);
                (_restoreWidth, _restoreHeight) = (window.Width, window.Height);
            }

            // Photino's own, which drops the frame as well as filling the monitor. Several of its
            // setters refuse to run once the window exists — Chromeless is one, which is why the
            // resize below was written — so this asks rather than assumes, and the resize stands
            // behind it unchanged.
            if (TryNativeFullScreen(window, fullScreen, logger))
            {
                _isFullScreen = fullScreen;
                Remember(window);
                return;
            }

            if (fullScreen)
            {
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

    /// <summary>How long to wait before each retry of the first connection, then this far apart.</summary>
    private static readonly TimeSpan[] ConnectBackoff =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    private static readonly TimeSpan ConnectRetryInterval = TimeSpan.FromSeconds(15);

    /// <summary>Keeps trying until the host answers or the window closes.</summary>
    /// <remarks>SignalR's auto-reconnect resumes an established connection; this one never was one.</remarks>
    private static async Task ConnectAsync(Microsoft.Extensions.Logging.ILogger logger, string serverUri, string screenId, byte[] authKey)
    {
        for (var attempt = 0; !_closing.IsCancellationRequested; attempt++)
        {
            try
            {
                await _ipc!.ConnectAsync(serverUri, screenId, authKey, _closing.Token);
                logger.LogInformation("Connected to IPC server");
                return;
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                var wait = attempt < ConnectBackoff.Length ? ConnectBackoff[attempt] : ConnectRetryInterval;

                // The first failure is the one worth a stack trace: a host that is simply not up
                // yet would otherwise fill the night's log with the same exception.
                if (attempt == 0)
                    logger.LogWarning(ex, "Could not reach the IPC server at {Uri}; retrying", serverUri);
                else
                    logger.LogDebug("Still could not reach {Uri} (attempt {Attempt}): {Message}", serverUri, attempt + 1, ex.Message);

                try
                {
                    await Task.Delay(wait, _closing.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>The host writes this file, its path handed in; absence means unprovisioned.</summary>
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

    /// <summary>Anchored to the executable, not the working directory.</summary>
    /// <remarks>A launched screen inherits the host's working directory; the log would land unseen.</remarks>
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

    /// <summary>Debug carries the raw per-tick state a page reports, the only view of a playhead.</summary>
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
