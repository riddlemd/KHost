using System.Drawing;
using Photino.NET;

namespace KHost.LocalScreen;

/// <summary>A window's place and size, in the window system's own units.</summary>
internal readonly record struct WindowBounds(int Left, int Top, int Width, int Height);

/// <summary>What the title bar drives, so its rules run without a native window.</summary>
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

internal sealed class PhotinoScreenWindow(PhotinoWindow window) : IScreenWindow
{
    public WindowBounds Bounds => new(window.Left, window.Top, window.Width, window.Height);

    public WindowBounds WorkArea
    {
        get
        {
            var area = Program.MonitorUnder(window).WorkArea;
            return new(area.X, area.Y, area.Width, area.Height);
        }
    }

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
}
