using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KHost.Screen2;

/// <summary>Where a screen's window was last left, so it comes back there.</summary>
internal sealed record WindowPlacement(int Left, int Top, int Width, int Height, bool FullScreen);

/// <summary>Keeps a screen's window placement on the machine the window is on, not the host.</summary>
/// <remarks>A screen elsewhere, or one started by hand, keeps its own place.</remarks>
internal sealed class WindowPlacementStore : IDisposable
{
    // Long enough that a drag writes once rather than per pixel, short enough that the host
    // killing the process (which is how it closes screens) rarely beats the write.
    private static readonly TimeSpan WriteDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Smaller than any window a host would leave itself, and smaller than Windows'
    /// minimized rect.</summary>
    private const int SmallestUsableSize = 200;

    /// <summary>Windows parks a minimized window at -32000,-32000. Nothing a host drags is
    /// anywhere near this, and a negative left is otherwise legitimate on a second monitor.</summary>
    private const int FurthestPlausibleOrigin = -10000;

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Timer _timer;

    private WindowPlacement? _pending;

    public WindowPlacementStore(string screenId, string baseDirectory, ILogger logger)
    {
        _logger = logger;
        _path = Path.Combine(baseDirectory, "cache", "screens", $"{SafeFileName(screenId)}.window.json");
        _timer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Null when nothing has been stored, or when what was stored cannot be read.</summary>
    public WindowPlacement? Read()
    {
        try
        {
            if (!File.Exists(_path))
                return null;

            var placement = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(_path));

            // Treated as nothing stored, so the window opens at the default instead. One that was
            // stored before this check existed is recovered the first time it is read.
            if (placement is null || !IsReachable(placement))
            {
                _logger.LogWarning(
                    "Ignoring an unreachable stored placement at {Path}; opening at the default instead", _path);
                return null;
            }

            return placement;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the window placement at {Path}", _path);
            return null;
        }
    }

    /// <summary>Records a placement to be written once the window stops moving.</summary>
    public void Schedule(WindowPlacement placement)
    {
        // The one choke point every save passes through. The move and size handlers fire as a
        // window is minimized, and Windows reports that as -32000,-32000 at title-bar size — so
        // without this the screen stores "minimized", restores there on the next launch, and no
        // click can ever bring it back. Dropping the save costs a position; keeping it costs the
        // window.
        if (!IsReachable(placement)) return;

        lock (_gate)
        {
            _pending = placement;
            _timer.Change(WriteDelay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        Flush();
    }

    /// <summary>Whether a placement could put the window somewhere a host can reach.</summary>
    internal static bool IsReachable(WindowPlacement placement)
        => placement.Width >= SmallestUsableSize
            && placement.Height >= SmallestUsableSize
            && placement.Left > FurthestPlausibleOrigin
            && placement.Top > FurthestPlausibleOrigin;

    private void Flush()
    {
        WindowPlacement? placement;

        lock (_gate)
        {
            placement = _pending;
            _pending = null;
        }

        if (placement is null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(placement));
        }
        catch (Exception ex)
        {
            // Losing a window position must never take the screen down mid-show.
            _logger.LogWarning(ex, "Could not save the window placement to {Path}", _path);
        }
    }

    /// <summary>A screen id is a host's free text: "Screen 1", or anything a host typed.</summary>
    internal static string SafeFileName(string screenId)
    {
        var safe = screenId.Trim();

        foreach (var invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');

        return string.IsNullOrEmpty(safe) ? "screen" : safe;
    }
}
