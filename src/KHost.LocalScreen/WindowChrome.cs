using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KHost.LocalScreen;

/// <summary>The window's own title bar and edges, which the page draws and this carries out.</summary>
/// <remarks>The window is chromeless on every OS, so moving, sizing, maximising and closing all
/// arrive as page messages. Maximise is a fill of the monitor's work area rather than the OS's own:
/// a borderless window has no zoom on macOS and covers the taskbar when Windows maximises it.
/// </remarks>
internal sealed class WindowChrome(IScreenWindow window, ILogger logger)
{
    /// <summary>Small enough for a corner of a laptop, large enough that the bar's buttons still fit.</summary>
    internal const int MinimumWidth = 320;

    internal const int MinimumHeight = 200;

    private readonly Lock _gate = new();

    private bool _fullScreen;
    private bool _maximised;
    private WindowBounds _restore;

    private Gesture? _gesture;

    /// <summary>Where page messages go. Null until the page is ready: a send into a web view
    /// with no page crashes the process.</summary>
    public Action<string>? SendToPage { get; set; }

    public bool IsMaximised { get { lock (_gate) return _maximised; } }

    /// <summary>Set by whoever drives full screen; the page hides the bar and edges while it is.</summary>
    public bool FullScreen
    {
        get { lock (_gate) return _fullScreen; }
        set
        {
            lock (_gate)
            {
                _fullScreen = value;
                _gesture = null;
            }

            PublishState();
        }
    }

    /// <summary>Tells the page what to draw: no bar in full screen, and which maximise glyph.</summary>
    public void PublishState()
    {
        bool fullScreen, maximised;
        lock (_gate) (fullScreen, maximised) = (_fullScreen, _maximised);

        SendToPage?.Invoke(JsonSerializer.Serialize(new { type = "window-state", fullScreen, maximised }));
    }

    /// <returns>False for a message that is not the title bar's.</returns>
    public bool Handle(JsonElement root)
    {
        var type = root.TryGetProperty("type", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        try
        {
            switch (type)
            {
                case "window-minimise":
                    window.Minimise();
                    return true;
                case "window-maximise":
                    ToggleMaximised();
                    return true;
                case "window-close":
                    logger.LogInformation("Closed from the title bar");
                    window.Close();
                    return true;
                case "window-drag":
                    Drag(root);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            // A window that cannot be moved this once still shows the song.
            logger.LogWarning(ex, "Could not carry out {Type}", type);
            return true;
        }
    }

    private void ToggleMaximised()
    {
        lock (_gate)
        {
            // The bar is hidden in full screen; a stale click must not resize a window that fills a monitor.
            if (_fullScreen) return;

            if (_maximised)
            {
                window.SetBounds(_restore);
                _maximised = false;
            }
            else
            {
                _restore = window.Bounds;
                window.SetBounds(window.WorkArea);
                _maximised = true;
            }
        }

        PublishState();
    }

    private void Drag(JsonElement root)
    {
        var phase = root.TryGetProperty("phase", out var p) ? p.GetString() : null;
        var screenX = Number(root, "screenX");
        var screenY = Number(root, "screenY");

        var unmaximised = false;

        lock (_gate)
        {
            if (_fullScreen) return;

            switch (phase)
            {
                case "start":
                    var edge = root.TryGetProperty("edge", out var e) && e.ValueKind == JsonValueKind.String
                        ? ParseEdge(e.GetString())
                        : Edges.None;

                    // A maximised window has no edges to pull; the page hides them, this makes sure.
                    if (edge != Edges.None && _maximised) return;

                    var bounds = window.Bounds;
                    var scale = UnitsPerCssPixel(bounds.Width, Number(root, "innerWidth"));

                    if (edge == Edges.None && _maximised)
                    {
                        bounds = Unmaximise(bounds, Number(root, "clientX"), Number(root, "innerWidth"), scale);
                        unmaximised = true;
                    }

                    _gesture = new Gesture(edge, bounds, screenX, screenY, scale);
                    break;

                case "move" when _gesture is { } g:
                    var dx = (int)Math.Round((screenX - g.ScreenX) * g.Scale);
                    var dy = (int)Math.Round((screenY - g.ScreenY) * g.Scale);

                    if (g.Edge == Edges.None)
                        window.SetLocation(g.Start.Left + dx, g.Start.Top + dy);
                    else
                        window.SetBounds(Resize(g.Start, g.Edge, dx, dy));
                    break;

                case "end":
                    _gesture = null;
                    break;
            }
        }

        if (unmaximised) PublishState();
    }

    /// <summary>Back to the size it had, with the pointer over the same share of the bar it grabbed.</summary>
    private WindowBounds Unmaximise(WindowBounds current, double clientX, double innerWidth, double scale)
    {
        var share = innerWidth > 0 ? Math.Clamp(clientX / innerWidth, 0, 1) : 0.5;
        var pointerX = current.Left + (clientX * scale);
        var left = (int)Math.Round(pointerX - (share * _restore.Width));

        var restored = _restore with { Left = left, Top = current.Top };
        window.SetBounds(restored);
        _maximised = false;

        return restored;
    }

    /// <summary>Converts a page's pointer movement into the window system's units.</summary>
    /// <remarks>Worked out from the window rather than assumed: macOS and GTK place windows in
    /// points, which a page's pixels already are, while Windows places them in device pixels. A
    /// chromeless window's page spans the whole window, so the page's width is the window's.
    /// Not outerWidth: WKWebView reports it as 0.</remarks>
    internal static double UnitsPerCssPixel(int windowWidth, double pageWidthCss)
        => pageWidthCss > 0 && windowWidth > 0 ? windowWidth / pageWidthCss : 1;

    /// <summary>Pulls the given edges by the pointer's travel, holding the opposite edges still.</summary>
    internal static WindowBounds Resize(WindowBounds start, Edges edge, int dx, int dy)
    {
        var (left, top, width, height) = (start.Left, start.Top, start.Width, start.Height);

        if (edge.HasFlag(Edges.Right))
            width = Math.Max(MinimumWidth, start.Width + dx);

        if (edge.HasFlag(Edges.Left))
        {
            width = Math.Max(MinimumWidth, start.Width - dx);
            left = start.Left + start.Width - width;
        }

        if (edge.HasFlag(Edges.Bottom))
            height = Math.Max(MinimumHeight, start.Height + dy);

        if (edge.HasFlag(Edges.Top))
        {
            height = Math.Max(MinimumHeight, start.Height - dy);
            top = start.Top + start.Height - height;
        }

        return new WindowBounds(left, top, width, height);
    }

    /// <summary>A compass name, as the page's edge elements carry it: "n", "se", and so on.</summary>
    internal static Edges ParseEdge(string? name)
    {
        var edges = Edges.None;

        foreach (var c in name ?? "")
        {
            edges |= c switch
            {
                'n' => Edges.Top,
                's' => Edges.Bottom,
                'w' => Edges.Left,
                'e' => Edges.Right,
                _ => Edges.None,
            };
        }

        return edges;
    }

    private static double Number(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private sealed record Gesture(Edges Edge, WindowBounds Start, double ScreenX, double ScreenY, double Scale);

    [Flags]
    internal enum Edges
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        Left = 4,
        Right = 8,
    }
}
