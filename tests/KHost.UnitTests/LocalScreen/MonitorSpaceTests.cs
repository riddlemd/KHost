using KHost.LocalScreen;

namespace KHost.UnitTests.LocalScreen;

/// <summary>Photino reports macOS monitors bottom-up and places windows top-down.</summary>
public class MonitorSpaceTests
{
    // Measured on a 2x Retina laptop (points) with a 1x 1920x1080 display to its right, 124pt lower.
    private static readonly (WindowBounds Area, WindowBounds WorkArea) RetinaReported =
        (new(0, 0, 1470, 956), new(0, 0, 1470, 922));

    private static readonly (WindowBounds Area, WindowBounds WorkArea) ExternalReported =
        (new(1470, -124, 1920, 1080), new(1470, -124, 1920, 1049));

    /// <summary>The overshoot: placed at the raw y of -124, AppKit pushes the window down to just
    /// under the external's menu bar, and a window sized to the monitor ends 31pt off its bottom.</summary>
    [Fact]
    public void ToWindowSpace_MacExternalMonitorBelowThePrimary_PutsTheWorkAreaUnderItsMenuBar()
    {
        var monitors = MonitorSpace.ToWindowSpace([RetinaReported, ExternalReported], yUp: true);

        Assert.Equal(new WindowBounds(1470, 0, 1920, 1080), monitors[1].Area);
        Assert.Equal(new WindowBounds(1470, 31, 1920, 1049), monitors[1].WorkArea);
        Assert.Equal(monitors[1].Area.Bottom, monitors[1].WorkArea.Bottom);
    }

    [Theory]
    // Retina primary, menu bar only: 34pt off the top.
    [InlineData(0, 0, 1470, 922, 956, 0, 34, 1470, 922)]
    // Retina primary with a 70pt dock: AppKit raises the work area's origin by the dock.
    [InlineData(0, 70, 1470, 852, 956, 0, 34, 1470, 852)]
    // A monitor stacked above the primary sits at a negative top.
    [InlineData(0, 956, 1920, 1080, 956, 0, -1080, 1920, 1080)]
    public void ToWindowSpace_MacRect_FlipsAboutThePrimarysHeight(
        int x, int y, int w, int h, int primaryHeight, int left, int top, int width, int height)
        => Assert.Equal(
            new WindowBounds(left, top, width, height),
            MonitorSpace.ToWindowSpace(new WindowBounds(x, y, w, h), primaryHeight, yUp: true));

    /// <summary>Windows reports monitors and places windows in the same device pixels, top-down,
    /// whatever each monitor's scale; the rects pass through.</summary>
    [Theory]
    [InlineData(0, 0, 2880, 1680)]       // 150% 1920x1080 with a taskbar, in device pixels
    [InlineData(2880, 0, 2400, 1290)]    // 125% 1920x1080 to its right
    [InlineData(-1920, -200, 1920, 1040)] // 100% to the left and higher
    public void ToWindowSpace_Windows_LeavesTheRectAlone(int x, int y, int w, int h)
    {
        var rect = new WindowBounds(x, y, w, h);
        var monitors = MonitorSpace.ToWindowSpace([(new(0, 0, 2880, 1728), new(0, 0, 2880, 1680)), (rect, rect)], yUp: false);

        Assert.Equal(rect, monitors[1].WorkArea);
    }

    [Fact]
    public void ToWindowSpace_PrimaryListedSecond_StillFlipsAboutThePrimary()
    {
        var monitors = MonitorSpace.ToWindowSpace([ExternalReported, RetinaReported], yUp: true);

        Assert.Equal(31, monitors[0].WorkArea.Top);
        Assert.Equal(34, monitors[1].WorkArea.Top);
    }

    [Fact]
    public void MonitorUnder_WindowOnAMonitorAboveThePrimary_FindsThatMonitor()
    {
        var above = (new WindowBounds(0, 956, 1920, 1080), new WindowBounds(0, 956, 1920, 1049));
        var monitors = MonitorSpace.ToWindowSpace([RetinaReported, above], yUp: true);

        // Raw, the upper monitor's rect spans y 956..2036 and this window, at y -900, matches neither.
        var monitor = MonitorSpace.MonitorUnder(new WindowBounds(100, -900, 1280, 720), monitors);

        // Its own menu bar takes the top 31pt of the 1080 above the primary.
        Assert.Equal(new WindowBounds(0, -1049, 1920, 1049), monitor.WorkArea);
    }

    [Fact]
    public void MonitorUnder_CentreOffEveryMonitor_TakesTheOneItOverlapsMost()
    {
        var monitors = MonitorSpace.ToWindowSpace([RetinaReported, ExternalReported], yUp: true);

        // Centre below both displays, but most of the window is on the external.
        var monitor = MonitorSpace.MonitorUnder(new WindowBounds(1400, 700, 1000, 900), monitors);

        Assert.Equal(1470, monitor.Area.Left);
    }

    [Fact]
    public void FullScreenArea_MacOS_IsTheWorkAreaBecauseTheMenuBarPinsTheWindow()
    {
        var external = MonitorSpace.ToWindowSpace([RetinaReported, ExternalReported], yUp: true)[1];

        Assert.Equal(external.WorkArea, MonitorSpace.FullScreenArea(external, menuBarPinsWindows: true));
        Assert.Equal(external.Area, MonitorSpace.FullScreenArea(external, menuBarPinsWindows: false));
    }

    private static readonly IReadOnlyList<ScreenMonitor> Monitors =
        MonitorSpace.ToWindowSpace([RetinaReported, ExternalReported], yUp: true);

    private static readonly WindowBounds Fallback = new(80, 80, 1280, 720);

    [Fact]
    public void Fit_InsideAWorkArea_IsUnchanged()
    {
        var remembered = new WindowBounds(1600, 100, 1280, 720);

        Assert.Equal(remembered, MonitorSpace.Fit(remembered, Monitors, Fallback));
    }

    /// <summary>The projector it was left on is unplugged.</summary>
    [Fact]
    public void Fit_OffEveryMonitor_OpensAtTheDefaultSizeCentredOnThePrimary()
    {
        var fitted = MonitorSpace.Fit(new WindowBounds(5000, 100, 1600, 900), Monitors, Fallback);

        Assert.Equal(new WindowBounds((1470 - 1280) / 2, 34 + ((922 - 720) / 2), 1280, 720), fitted);
    }

    /// <summary>The maximised rect stored as a window by an older build is larger than the
    /// laptop's work area once the external is gone.</summary>
    [Fact]
    public void Fit_LargerThanItsWorkArea_OpensAtTheDefaultSizeInsideIt()
    {
        var fitted = MonitorSpace.Fit(new WindowBounds(0, 34, 1470, 1000), Monitors, Fallback);

        Assert.Equal(new WindowBounds(0, 34, 1280, 720), fitted);
    }

    [Fact]
    public void Fit_PartlyOffTheBottomOfItsMonitor_IsPulledInside()
    {
        var fitted = MonitorSpace.Fit(new WindowBounds(1470, 500, 1920, 1049), Monitors, Fallback);

        Assert.Equal(new WindowBounds(1470, 31, 1920, 1049), fitted);
    }

    [Fact]
    public void Fit_NoMonitorsReported_LeavesTheWindowWhereItWas()
    {
        var remembered = new WindowBounds(5000, 5000, 1280, 720);

        Assert.Equal(remembered, MonitorSpace.Fit(remembered, [], Fallback));
    }

    [Theory]
    [InlineData(-50, 10, 100, 100, 0, 10, 100, 100)]
    [InlineData(950, 10, 100, 100, 900, 10, 100, 100)]
    [InlineData(10, -50, 100, 100, 10, 0, 100, 100)]
    [InlineData(10, 950, 100, 100, 10, 500, 100, 100)]
    [InlineData(10, 10, 2000, 100, 0, 10, 1000, 100)]
    [InlineData(10, 10, 100, 2000, 10, 0, 100, 600)]
    public void ClampInto_AnyRect_EndsWhollyInsideTheArea(
        int x, int y, int w, int h, int left, int top, int width, int height)
        => Assert.Equal(
            new WindowBounds(left, top, width, height),
            MonitorSpace.ClampInto(new WindowBounds(x, y, w, h), new WindowBounds(0, 0, 1000, 600)));
}
