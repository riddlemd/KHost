using System.Drawing;
using Photino.NET;

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

/// <summary>Monitor geometry in the window's own space, and the rules that place a window on it.</summary>
internal static class MonitorSpace
{
    /// <summary>Moves a monitor rect Photino reported into the space window positions are in.</summary>
    /// <remarks>On macOS Photino hands back AppKit's screen rects as they are — origin at the primary
    /// monitor's bottom-left, y growing upwards — while it places windows from the top-left, y
    /// growing down. Used unflipped, a monitor offset vertically from the primary gets a top that
    /// is really its bottom, and AppKit then nudges the window below the menu bar, pushing its
    /// bottom off the display. Both are points on macOS and device pixels on Windows, so only the
    /// axis differs, never the scale.</remarks>
    internal static WindowBounds ToWindowSpace(WindowBounds reported, int primaryHeight, bool yUp)
        => yUp ? reported with { Top = primaryHeight - reported.Bottom } : reported;

    /// <summary>All of Photino's monitors, in window space.</summary>
    internal static IReadOnlyList<ScreenMonitor> ToWindowSpace(
        IReadOnlyList<(WindowBounds Area, WindowBounds WorkArea)> reported, bool yUp)
    {
        if (reported.Count == 0) return [];

        // AppKit's primary is the one at the origin; every other screen is placed relative to it.
        var primary = reported.FirstOrDefault(m => m.Area.Left == 0 && m.Area.Top == 0, reported[0]);
        var height = primary.Area.Height;

        return reported
            .Select(m => new ScreenMonitor(ToWindowSpace(m.Area, height, yUp), ToWindowSpace(m.WorkArea, height, yUp)))
            .ToList();
    }

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

    /// <summary>What a window grown to full screen can cover.</summary>
    /// <remarks>AppKit pins a window's top below the menu bar however it is placed, so a window the
    /// monitor's full height is pushed down and its bottom edge — where a QR code sits — is off the
    /// display. On macOS the grown window therefore fills the work area.</remarks>
    internal static WindowBounds FullScreenArea(ScreenMonitor monitor, bool menuBarPinsWindows)
        => menuBarPinsWindows ? monitor.WorkArea : monitor.Area;

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
        // The primary sits at the origin on every OS once in window space.
        var primary = monitors.FirstOrDefault(m => m.Area.Left == 0 && m.Area.Top == 0, monitors[0]);
        var work = onSomeMonitor ? MonitorUnder(remembered, monitors).WorkArea : primary.WorkArea;

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

    private static long Overlap(WindowBounds a, WindowBounds b)
    {
        long w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        long h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return w > 0 && h > 0 ? w * h : 0;
    }
}

internal sealed class PhotinoScreenWindow(PhotinoWindow window) : IScreenWindow
{
    public WindowBounds Bounds => new(window.Left, window.Top, window.Width, window.Height);

    public WindowBounds WorkArea => MonitorSpace.MonitorUnder(Bounds, Monitors).WorkArea;

    public IReadOnlyList<ScreenMonitor> Monitors
        => MonitorSpace.ToWindowSpace(
            window.Monitors.Select(m => (ToBounds(m.MonitorArea), ToBounds(m.WorkArea))).ToList(),
            OperatingSystem.IsMacOS());

    // Size before place: shrinking at the old origin first never pushes an edge off the monitor.
    public void SetBounds(WindowBounds bounds)
    {
        window.SetSize(new Size(bounds.Width, bounds.Height));
        window.SetLocation(new Point(bounds.Left, bounds.Top));
    }

    public void SetLocation(int left, int top) => window.SetLocation(new Point(left, top));

    public void Minimise() => window.SetMinimized(true);

    // The same exit the OS close button took: WaitForClose returns and Main's cleanup runs.
    public void Close() => window.Close();

    private static WindowBounds ToBounds(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
