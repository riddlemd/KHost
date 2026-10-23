using System.Text.Json;
using System.Text.Json.Nodes;
using KHost.IPC.SignalR;
using Microsoft.Extensions.Logging;
using Oriel;
using Serilog;
using Serilog.Events;
using KHost.Common.Storage;

namespace KHost.LocalScreen;

internal static class Program
{
    /// <summary>The one IPC channel between the page and this process; preload.js names it too.</summary>
    private const string PageChannel = "screen";

    private static ScreenIpcController? _ipc;
    private static WindowPlacementStore? _placement;
    private static WindowChrome? _chrome;
    private static OrielScreenWindow? _screenWindow;

    /// <summary>A first run's window: small enough to fit any monitor rather than fill whatever it landed on.</summary>
    private static readonly WindowBounds DefaultBounds = new(80, 80, 1280, 720);
    private static readonly CancellationTokenSource _closing = new();

    private static bool _isFullScreen;
    private static int _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;

    [STAThread]
    private static int Main(string[] args)
    {
        var serverUri = GetArg(args, "--server-uri") ?? "http://localhost:5000/ipc/screen";
        var screenId = GetArg(args, "--screen-id") ?? Environment.MachineName;

        var app = OrielApp.Create("KHost Screen");
        var authKey = ReadAuthKey(GetArg(args, "--key-file"));

        var logLevel = ParseLogLevel(GetArg(args, "--log-level"));

        using var loggerFactory = LoggerFactory.Create(b => b
            .SetMinimumLevel(logLevel)
            .AddSerilog(CreateSerilog(screenId, logLevel), dispose: true));

        var logger = loggerFactory.CreateLogger("LocalScreen");

        var player = new StreamMediaPlayer(loggerFactory.CreateLogger<StreamMediaPlayer>());

        _ipc = new ScreenIpcController(
            ProjectExtensions.CreateScreenClient(loggerFactory),
            player,
            loggerFactory.CreateLogger<ScreenIpcController>());

        _placement = new WindowPlacementStore(screenId, AppPaths.DataRoot, logger);

        // Where this screen was last left; checked against the monitors once the window exists.
        var stored = _placement.Read();
        var (left, top, width, height) = stored?.Bounds ?? DefaultBounds;

        // Seeded so leaving full screen returns to the remembered window, not to 0x0.
        (_restoreLeft, _restoreTop, _restoreWidth, _restoreHeight) = (left, top, width, height);

        logger.LogInformation(
            stored is null
                ? "No stored window placement; opening at {Left},{Top} {Width}x{Height}"
                : "Restoring window placement {Left},{Top} {Width}x{Height} (full screen {FullScreen}, maximised {Maximised})",
            left, top, width, height, stored?.FullScreen ?? false, stored?.Maximised ?? false);

        // Every screen is its own process, and two on one profile would fight over Chromium's lock on it.
        app.SetPath("userData", ProfileDirectory(screenId));
        // Songs start on a command from the host, never on a click in this window.
        app.CommandLine.AppendSwitch("autoplay-policy", "no-user-gesture-required");

        BrowserWindow? window = null;
        var ready = false;

        void SendToPage(string json) => window!.WebContents.Send(PageChannel, JsonValue.Create(json));

        IpcMain.On(PageChannel, e =>
        {
            if (e.Args is not [{ ValueKind: JsonValueKind.String } argument]) return;
            var message = argument.GetString()!;

            // The page saying it is wired up: nothing is sent to it before this.
            if (message.Contains("\"ready\"", StringComparison.Ordinal) && !ready)
            {
                ready = true;
                var openCodecsOnly = ReadsOpenCodecsOnly(message);

                player.SendToBrowser = SendToPage;
                _chrome!.SendToPage = SendToPage;
                _chrome.PublishState();

                _ = ConnectUntilUpAsync(
                    cancellationToken => _ipc!.ConnectAsync(serverUri, screenId, authKey, openCodecsOnly, cancellationToken),
                    () => _ipc!.LastRefusalReason, logger, serverUri, Task.Delay, _closing.Token);
                // Position keeps the host's playhead live between commands, which only report
                // on completion; the resync keeps those reports' stamps honest as clocks drift.
                _ = RepeatAsync(TimeSpan.FromSeconds(1), () => _ipc!.SendCurrentStateAsync(), logger, _closing.Token);
                _ = RepeatAsync(TimeSpan.FromMinutes(5), () => _ipc!.ResyncClockAsync(), logger, _closing.Token);

                // Before full screen, and here rather than at construction because both need a
                // window that exists — the page being ready is the first moment that is true.
                var movedToSecondary = FitOntoMonitors(_screenWindow!, logger);

                // After the fit, so un-maximising returns to a rect that is on a monitor now.
                if (stored?.Maximised == true && !movedToSecondary)
                    _chrome!.Maximise();

                // Unreadable monitors count as one: a window is the safe way to open.
                int monitorCount;
                try { monitorCount = _screenWindow!.Monitors.Count; }
                catch { monitorCount = 1; }

                if (MonitorSpace.LaunchesFullScreen(stored?.FullScreen == true, movedToSecondary, monitorCount))
                    SetFullScreen(window!, true, logger);
                else if (stored?.FullScreen == true)
                    logger.LogInformation("Opening as a window: full screen waits for a second monitor");

                return;
            }

            // Parsed once here: the player and the window each route on the same type.
            JsonDocument document;
            try { document = JsonDocument.Parse(message); }
            catch (JsonException) { return; }

            using (document)
            {
                var root = document.RootElement;
                if (!player.HandleBrowserMessage(root, message) && !_chrome!.Handle(root))
                    HandleWindowMessage(window!, root, logger);
            }
        });

        app.Ready += (_, _) =>
        {
            var page = WritePlayerPage();

            window = new BrowserWindow(new BrowserWindowOptions
            {
                Title = "KHost Screen",
                // The page draws the window buttons, a drag strip along the top and the edges
                // (title-bar.js), the same on every OS, and hides them in full screen.
                Frame = false,
                X = left,
                Y = top,
                Width = width,
                Height = height,
                BackgroundColor = "#000000",
                Icon = AppIcon.ResolveWindowIcon(AppContext.BaseDirectory, AppIcon.CurrentPlatform),
                WebPreferences = new WebPreferences
                {
                    Preload = page.Preload,
#if !DEBUG
                    DevTools = false,
#endif
                },
            });

            // Saved as it moves, not on the way out: the host closes a screen by killing the
            // process, so an exit handler would never run on the ordinary path.
            window.Moved += (_, _) => Remember(window);
            window.Resized += (_, _) => Remember(window);
            window.Maximized += (_, _) => Remember(window);
            window.Unmaximized += (_, _) => Remember(window);

            _screenWindow = new OrielScreenWindow(window);
            _chrome = new WindowChrome(_screenWindow, logger);

            MacDockIcon.TrySet(logger);

            // A file rather than a string: a file page is a secure context, which secure-context
            // gated APIs need. The file is the same page, written once per run.
            window.LoadUrl(new Uri(page.Page).AbsoluteUri);

            logger.LogInformation("LocalScreen starting: server={ServerUri} screen={ScreenId}", serverUri, screenId);
        };

        // Returns once the window has closed: the last window closing quits the app.
        var exitCode = app.Run();

        // Stops the connect retry loop, which otherwise keeps a closing screen alive waiting on
        // its next delay.
        _closing.Cancel();

        if (_pagePath is { } pageDirectory)
        {
            try { Directory.Delete(pageDirectory, recursive: true); }
            catch (Exception ex) { logger.LogDebug(ex, "Could not remove the page at '{Path}'", pageDirectory); }
        }

        _placement.Dispose();
        _ipc.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Log.CloseAndFlush();
        _closing.Dispose();

        return exitCode;
    }

    /// <summary>Chromium's profile for one screen, outside the binary's folder: a signed bundle is read-only.</summary>
    internal static string ProfileDirectory(string screenId)
    {
        var safe = string.Concat(screenId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(Path.GetTempPath(), "khost-screen", "profiles", safe);
    }

    /// <summary>Moves a remembered window onto the monitors there are now; see <see cref="MonitorSpace.Fit"/>
    /// and <see cref="MonitorSpace.OntoSecondary"/>.</summary>
    /// <returns>Whether the window was carried off the primary onto another monitor.</returns>
    private static bool FitOntoMonitors(OrielScreenWindow window, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            var current = window.Bounds;
            var monitors = window.Monitors;
            var fitted = MonitorSpace.Fit(current, monitors, DefaultBounds);

            if (fitted != current)
                logger.LogWarning("The stored placement {Stored} does not fit the monitors there are now; opening at {Fitted}",
                    current, fitted);

            var placed = MonitorSpace.OntoSecondary(fitted, monitors);

            if (placed != fitted)
                logger.LogInformation("Opening on the second monitor at {Placed} rather than the primary at {Fitted}", placed, fitted);

            if (placed != current) window.SetBounds(placed);

            return placed != fitted;
        }
        catch (Exception ex)
        {
            // Never worth failing a screen over: a window in an odd place still shows the song.
            logger.LogWarning(ex, "Could not check the window is on a monitor");
            return false;
        }
    }

    /// <summary>The page's 'ready' message saying its engine decodes no H.264 or AAC.</summary>
    /// <remarks>False when the message cannot be read or does not say: every engine before Oriel
    /// decoded H.264, so that is what a silent page gets.</remarks>
    internal static bool ReadsOpenCodecsOnly(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("openCodecsOnly", out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Where the page was written, so it can be cleaned up when the screen closes.</summary>
    private static string? _pagePath;

    /// <summary>Writes the built page and its preload beside the screen's own temp state and answers their paths.</summary>
    internal static (string Page, string Preload) WritePlayerPage()
    {
        var root = Path.Combine(Path.GetTempPath(), "khost-screen");
        var directory = Path.Combine(root, Environment.ProcessId.ToString());

        SweepAbandonedPages(root);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "player.html");
        File.WriteAllText(path, BuildPlayerPage());

        var preload = Path.Combine(directory, "preload.js");
        File.WriteAllText(preload, ReadResource("screen-ui/preload.js"));

        _pagePath = directory;
        return (path, preload);
    }

    /// <summary>Removes pages left by screens that are no longer running.</summary>
    private static void SweepAbandonedPages(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (!int.TryParse(Path.GetFileName(directory), out var pid)) continue;
                if (pid == Environment.ProcessId) continue;

                try { _ = System.Diagnostics.Process.GetProcessById(pid); continue; }
                catch (ArgumentException) { }

                try { Directory.Delete(directory, recursive: true); } catch { }
            }
        }
        catch { }
    }

    /// <summary>Builds the player page with its script inlined, from the embedded resources.</summary>
    internal static string BuildPlayerPage()
    {
        var html = ReadResource("screen-ui/index.html");

        // The console's own module: one list of browser keys cancelled, for every KHost window.
        html = Inline(html, "browser-keys.js");
        // hls.js before player.js, which reads Hls at load time to pick its playback path.
        html = Inline(html, "hls.light.min.js");
        html = Inline(html, "title-bar.js");
        // Order here is immaterial: each call swaps a tag for its script where the tag already
        // sits, so the page's own tag order is what decides what is defined first.
        html = Inline(html, "lyrics-overlay.js");
        html = Inline(html, "intro-card.js");
        html = Inline(html, "lead-in.js");
        html = Inline(html, "stem-mixer.js");
        html = Inline(html, "wake-watch.js");
        html = Inline(html, "butterchurn.min.js");
        html = Inline(html, "visualiser-presets.js");
        html = Inline(html, "eq-visualisers.js");
        html = Inline(html, "visualiser.js");
        html = Inline(html, "screen-chrome.js");
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

    /// <summary>Full screen and maximised are remembered as flags over the windowed rect; see <see cref="WindowPlacement"/>.</summary>
    private static void Remember(BrowserWindow window)
    {
        if (_placement is null || _screenWindow is null) return;

        try
        {
            _placement.Schedule(WindowPlacement.Capture(
                _screenWindow.Bounds,
                _isFullScreen,
                new WindowBounds(_restoreLeft, _restoreTop, _restoreWidth, _restoreHeight),
                _chrome?.IsMaximised ?? false,
                _chrome?.RestoreBounds ?? default));
        }
        catch
        {
            // Reading the window can throw while it is being torn down; a lost position is nothing.
        }
    }

    /// <summary>Handles the page messages that drive the window rather than the player.</summary>
    private static void HandleWindowMessage(BrowserWindow window, JsonElement root, Microsoft.Extensions.Logging.ILogger logger)
    {
        var type = root.TryGetProperty("type", out var p) ? p.GetString() : null;

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

    /// <summary>Fills the monitor the window is on with the picture alone.</summary>
    /// <remarks>On macOS the simple full screen: native full screen moves the window to a Space of
    /// its own and blanks every other display, so the host loses the console it runs the show from.
    /// </remarks>
    private static void SetFullScreen(BrowserWindow window, bool fullScreen, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            if (fullScreen)
                (_restoreLeft, _restoreTop, _restoreWidth, _restoreHeight) = _screenWindow!.Bounds;

            if (OperatingSystem.IsMacOS()) window.SetSimpleFullScreen(fullScreen);
            else window.SetFullScreen(fullScreen);

            _isFullScreen = fullScreen;
            _chrome?.FullScreen = fullScreen;
            logger.LogInformation("Full screen {FullScreen}; window now {Bounds}", fullScreen, _screenWindow!.Bounds);
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
    /// <remarks>Only the first connection: once one is up, the client wins back a closed link
    /// itself, so returning here leaves one reconnect path rather than two.</remarks>
    internal static async Task ConnectUntilUpAsync(
        Func<CancellationToken, Task> connectAsync,
        Func<string?> lastRefusalReason,
        Microsoft.Extensions.Logging.ILogger logger,
        string serverUri,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await connectAsync(cancellationToken);
                logger.LogInformation("Connected to IPC server");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                var wait = attempt < ConnectBackoff.Length ? ConnectBackoff[attempt] : ConnectRetryInterval;

                // A refusal reached the host and was told no — not a network problem, and the
                // stack trace below is one that never gets fixed by retrying.
                if (lastRefusalReason() is { } reason)
                {
                    if (attempt == 0)
                        logger.LogWarning("Registration refused by the host: {Reason}", reason);
                    else
                        logger.LogDebug("Still refused by the host: {Reason}", reason);
                }
                // The first failure is the one worth a stack trace: a host that is simply not up
                // yet would otherwise fill the night's log with the same exception.
                else if (attempt == 0)
                    logger.LogWarning(ex, "Could not reach the IPC server at {Uri}; retrying", serverUri);
                else
                    logger.LogDebug("Still could not reach {Uri} (attempt {Attempt}): {Message}", serverUri, attempt + 1, ex.Message);

                try
                {
                    await delayAsync(wait, cancellationToken);
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

    /// <summary>Runs <paramref name="action"/> every <paramref name="interval"/> until cancelled.</summary>
    /// <remarks>A throw is logged and the next tick runs anyway: one failed send must not end
    /// position reports for the rest of the night.</remarks>
    internal static async Task RepeatAsync(
        TimeSpan interval, Func<Task> action, Microsoft.Extensions.Logging.ILogger logger, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "A repeating screen task failed; it runs again next tick");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Anchored to the executable, not the working directory.</summary>
    /// <remarks>A launched screen inherits the host's working directory; the log would land unseen.</remarks>
    private static Serilog.Core.Logger CreateSerilog(string screenId, LogLevel minimum)
    {
        var logDirectory = AppPaths.Logs;
        Directory.CreateDirectory(logDirectory);
        Telemetry.KHostLogFiles.SweepStaleLogs(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Is(ToSerilog(minimum))
            .WriteTo.Console()
            .WriteTo.File(
                // Own file per launch (pid breaks a same-second tie), so a relaunch never shares —
                // and never fights over — the file a still-closing previous run was writing.
                path: Path.Combine(logDirectory, Telemetry.KHostLogFiles.ScreenFileName(screenId, Environment.ProcessId)),
                rollingInterval: RollingInterval.Infinite,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 10_000_000,
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
