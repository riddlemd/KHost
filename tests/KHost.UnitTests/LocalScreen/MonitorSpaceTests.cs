using KHost.LocalScreen;

namespace KHost.UnitTests.LocalScreen;

/// <summary>Monitors as Oriel reports them: DIPs, top-down from the primary's top-left.</summary>
public class MonitorSpaceTests
{
    // Measured on a 2x Retina laptop (points) with a 1x 1920x1080 display to its right; each work
    // area starts under its own menu bar.
    private static readonly ScreenMonitor Retina = new(new(0, 0, 1470, 956), new(0, 34, 1470, 922));

    private static readonly ScreenMonitor External = new(new(1470, 0, 1920, 1080), new(1470, 31, 1920, 1049));

    private static readonly IReadOnlyList<ScreenMonitor> Monitors = [Retina, External];

    private static readonly WindowBounds Fallback = new(80, 80, 1280, 720);

    [Fact]
    public void MonitorUnder_WindowOnAMonitorAboveThePrimary_FindsThatMonitor()
    {
        var above = new ScreenMonitor(new(0, -1080, 1920, 1080), new(0, -1049, 1920, 1049));

        var monitor = MonitorSpace.MonitorUnder(new WindowBounds(100, -900, 1280, 720), [Retina, above]);

        Assert.Equal(above, monitor);
    }

    [Fact]
    public void MonitorUnder_CentreOffEveryMonitor_TakesTheOneItOverlapsMost()
    {
        // Centre below both displays, but most of the window is on the external.
        var monitor = MonitorSpace.MonitorUnder(new WindowBounds(1400, 700, 1000, 900), Monitors);

        Assert.Equal(1470, monitor.Area.Left);
    }

    /// <summary>A centre on the seam between two side-by-side monitors is on the one to the right.</summary>
    [Fact]
    public void MonitorUnder_CentreOnTheSharedEdge_IsTheMonitorThatStartsThere()
    {
        // Centre x = 1270 + 400 / 2 = 1470, the external's left edge and the primary's right.
        var monitor = MonitorSpace.MonitorUnder(new WindowBounds(1270, 100, 400, 300), Monitors);

        Assert.Equal(1470, monitor.Area.Left);
    }

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

    /// <summary>Alone, a full-screen screen would cover the console the show is driven from.</summary>
    [Theory]
    [InlineData(true, false, 1, false)]
    [InlineData(true, false, 2, true)]
    [InlineData(false, true, 2, true)]
    [InlineData(false, false, 2, false)]
    [InlineData(false, false, 1, false)]
    public void LaunchesFullScreen_DependsOnASecondMonitor(bool remembered, bool moved, int monitors, bool expected)
        => Assert.Equal(expected, MonitorSpace.LaunchesFullScreen(remembered, moved, monitors));

    [Fact]
    public void OntoSecondary_OnThePrimary_CentresItOnTheOtherMonitorAtItsOwnSize()
    {
        var placed = MonitorSpace.OntoSecondary(new WindowBounds(80, 80, 1280, 720), Monitors);

        Assert.Equal(new WindowBounds(1470 + ((1920 - 1280) / 2), 31 + ((1049 - 720) / 2), 1280, 720), placed);
    }

    [Fact]
    public void OntoSecondary_AlreadyOnTheOtherMonitor_IsUnchanged()
    {
        var window = new WindowBounds(1600, 100, 1280, 720);

        Assert.Equal(window, MonitorSpace.OntoSecondary(window, Monitors));
    }

    [Fact]
    public void OntoSecondary_OnlyOneMonitor_IsUnchanged()
    {
        var window = new WindowBounds(80, 80, 1280, 720);

        Assert.Equal(window, MonitorSpace.OntoSecondary(window, [Retina]));
    }

    [Fact]
    public void OntoSecondary_PrimaryListedSecond_StillMovesOffThePrimary()
    {
        var placed = MonitorSpace.OntoSecondary(new WindowBounds(80, 80, 1280, 720), [External, Retina]);

        Assert.Equal(1470 + ((1920 - 1280) / 2), placed.Left);
    }

    [Fact]
    public void OntoSecondary_LargerThanTheOtherWorkArea_IsShrunkInsideIt()
    {
        var placed = MonitorSpace.OntoSecondary(new WindowBounds(0, 0, 2000, 1200), Monitors);

        Assert.Equal(new WindowBounds(1470, 31, 1920, 1049), placed);
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
