using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KHost.Screen2;

/// <summary>Where a screen's window was last left, so it comes back there.</summary>
internal sealed record WindowPlacement(int Left, int Top, int Width, int Height, bool FullScreen);

/// <summary>
/// Keeps a screen's window placement on the machine the window is on, not on the host: a screen
/// on another machine keeps its own place, and one started by hand remembers as much as one the
/// host launched.
/// </summary>
internal sealed class WindowPlacementStore : IDisposable
{
    // Long enough that a drag writes once rather than per pixel, short enough that the host
    // killing the process — which is how it closes screens — rarely beats the write.
    private static readonly TimeSpan WriteDelay = TimeSpan.FromMilliseconds(400);

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

            // A zero-sized window is invisible and unrecoverable without deleting the file, so a
            // stored one is treated as nothing stored.
            return placement is { Width: > 0, Height: > 0 } ? placement : null;
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

    /// <summary>A screen id is a host's free text — "Screen 1", or anything a host typed.</summary>
    internal static string SafeFileName(string screenId)
    {
        var safe = screenId.Trim();

        foreach (var invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');

        return string.IsNullOrEmpty(safe) ? "screen" : safe;
    }
}
