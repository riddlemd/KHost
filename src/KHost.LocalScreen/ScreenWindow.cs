using Oriel;

namespace KHost.LocalScreen;

/// <summary>A window's place and size, in the window system's own units.</summary>
internal readonly record struct WindowBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}

/// <summary>One monitor, in the same space a window is placed in.</summary>
/// <param name="WorkArea">The menu bar, dock and taskbar excluded.</param>
internal readonly record struct ScreenMonitor(WindowBounds Area, WindowBounds WorkArea);

/// <summary>What the window controls drive, so its rules run without a native window.</summary>
internal interface IScreenWindow
{
    WindowBounds Bounds { get; }

    /// <summary>The usable area of the monitor the window is on: the menu bar and taskbar excluded.</summary>
    WindowBounds WorkArea { get; }

    void SetBounds(WindowBounds bounds);

    void SetLocation(int left, int top);

    void Minimise();

    void Close();
}

/// <summary>The rules that place a window on the monitors, all in Oriel's DIPs from the primary's top-left.</summary>
internal static class MonitorSpace
{
    /// <summary>The monitor under the window's centre, else the one it overlaps most, else the first.</summary>
    /// <remarks>A venue drives the console on one display and the screen on a television; taking
    /// the first monitor would drag the screen off the television and onto the console mid-show.
    /// </remarks>
    internal static ScreenMonitor MonitorUnder(WindowBounds window, IReadOnlyList<ScreenMonitor> monitors)
    {
        var centreX = window.Left + (window.Width / 2);
        var centreY = window.Top + (window.Height / 2);

        foreach (var monitor in monitors)
        {
            var a = monitor.Area;
            if (centreX >= a.Left && centreX < a.Right && centreY >= a.Top && centreY < a.Bottom)
                return monitor;
        }

        return monitors.MaxBy(m => Overlap(window, m.WorkArea));
    }

    /// <summary>Where a remembered window should open on the monitors there are now.</summary>
    /// <remarks>A remembered rect can be perfectly sensible and still land nowhere: a venue runs the
    /// screen on a projector, unplugs it, and the next launch restores onto coordinates that no
    /// longer exist. Off every monitor it opens at <paramref name="fallback"/>'s size, centred on
    /// the primary; larger than its monitor's work area it opens at that size where it was; and
    /// either way it is pulled wholly inside the work area so no corner is out of reach.</remarks>
    internal static WindowBounds Fit(WindowBounds remembered, IReadOnlyList<ScreenMonitor> monitors, WindowBounds fallback)
    {
        if (monitors.Count == 0) return remembered;

        var onSomeMonitor = monitors.Any(m => Overlap(remembered, m.WorkArea) > 0);
        var work = onSomeMonitor ? MonitorUnder(remembered, monitors).WorkArea : Primary(monitors).WorkArea;

        var bounds = remembered;

        if (!onSomeMonitor)
        {
            bounds = fallback with
            {
                Left = work.Left + ((work.Width - fallback.Width) / 2),
                Top = work.Top + ((work.Height - fallback.Height) / 2),
            };
        }
        else if (remembered.Width > work.Width || remembered.Height > work.Height)
        {
            bounds = fallback with { Left = remembered.Left, Top = remembered.Top };
        }

        return ClampInto(bounds, work);
    }

    /// <summary>Whether a launching window goes full screen.</summary>
    /// <remarks>Only with a second monitor: alone, the screen would cover the console the host drives
    /// the show from, so it opens as a window whatever it was left as.</remarks>
    internal static bool LaunchesFullScreen(bool rememberedFullScreen, bool movedToSecondary, int monitorCount)
        => monitorCount > 1 && (rememberedFullScreen || movedToSecondary);

    /// <summary>Where a launching window goes when there is a monitor besides the primary.</summary>
    /// <remarks>The primary carries the console, so a window on it moves to the first other monitor
    /// listed, keeping its size, centred and inside that work area. A window already on another
    /// monitor stays, so a venue with three displays keeps the one it chose.</remarks>
    internal static WindowBounds OntoSecondary(WindowBounds window, IReadOnlyList<ScreenMonitor> monitors)
    {
        if (monitors.Count < 2) return window;

        var primary = Primary(monitors);
        if (MonitorUnder(window, monitors) != primary) return window;

        var work = monitors.First(m => m != primary).WorkArea;
        return ClampInto(
            window with
            {
                Left = work.Left + ((work.Width - window.Width) / 2),
                Top = work.Top + ((work.Height - window.Height) / 2),
            },
            work);
    }

    /// <summary>No larger than the area, and moved the least distance to sit wholly inside it.</summary>
    internal static WindowBounds ClampInto(WindowBounds bounds, WindowBounds area)
    {
        var width = Math.Min(bounds.Width, area.Width);
        var height = Math.Min(bounds.Height, area.Height);

        return new WindowBounds(
            Math.Clamp(bounds.Left, area.Left, area.Right - width),
            Math.Clamp(bounds.Top, area.Top, area.Bottom - height),
            width,
            height);
    }

    // The primary sits at the origin on every OS once in window space.
    private static ScreenMonitor Primary(IReadOnlyList<ScreenMonitor> monitors)
        => monitors.FirstOrDefault(m => m.Area.Left == 0 && m.Area.Top == 0, monitors[0]);

    private static long Overlap(WindowBounds a, WindowBounds b)
    {
        long w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        long h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return w > 0 && h > 0 ? w * h : 0;
    }
}

/// <remarks>Oriel reports windows and displays alike in DIPs from the primary's top-left, so its
/// rects are window space as they come.</remarks>
internal sealed class OrielScreenWindow(BrowserWindow window) : IScreenWindow
{
    public WindowBounds Bounds => ToBounds(window.GetBounds());

    public WindowBounds WorkArea => MonitorSpace.MonitorUnder(Bounds, Monitors).WorkArea;

    public IReadOnlyList<ScreenMonitor> Monitors
        => [.. Screen.GetAllDisplays().Select(d => new ScreenMonitor(ToBounds(d.Bounds), ToBounds(d.WorkArea)))];

    public void SetBounds(WindowBounds bounds)
        => window.SetBounds(new Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height));

    public void SetLocation(int left, int top) => window.SetPosition(left, top);

    public void Minimise() => window.Minimize();

    // The same exit the OS close button takes: the last window closing ends OrielApp.Run.
    public void Close() => window.Close();

    private static WindowBounds ToBounds(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
