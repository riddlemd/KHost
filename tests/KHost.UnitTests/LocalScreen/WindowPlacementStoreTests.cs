using System.Text.Json;
using KHost.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.LocalScreen;

/// <summary>The host kills the process to close a screen, so writes schedule as the window moves.</summary>
public class WindowPlacementStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"khost-placement-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Read_NothingStored_IsNull()
        => Assert.Null(Store("Screen 1").Read());

    [Fact]
    public void Schedule_ThenDispose_WritesWhatWasScheduled()
    {
        using (var store = Store("Screen 1"))
            store.Schedule(new WindowPlacement(120, 80, 1600, 900, false));

        var placement = Store("Screen 1").Read();

        Assert.Equal(new WindowPlacement(120, 80, 1600, 900, false), placement);
    }

    /// <summary>Full screen is a flag, not the monitor's pixels, so it may come back elsewhere.</summary>
    [Fact]
    public void Schedule_FullScreen_RemembersTheFlagAndTheWindowUnderneath()
    {
        using (var store = Store("Screen 1"))
            store.Schedule(new WindowPlacement(10, 20, 1280, 720, true));

        var placement = Store("Screen 1").Read();

        Assert.True(placement!.FullScreen);
        Assert.Equal(1280, placement.Width);
    }

    /// <summary>Windows reports a minimized window at -32000,-32000 with a title-bar-sized rect,
    /// and the move handler fires as it minimizes. Storing that restores the window there on every
    /// later launch, where no click can reach it.</summary>
    [Theory]
    [InlineData(-32000, -32000, 160, 39)]   // exactly what Windows reports for a minimized window
    [InlineData(-32000, -32000, 1280, 720)] // minimized origin, ordinary size
    [InlineData(80, 80, 160, 39)]           // on screen, but too small to grab
    public void Schedule_AnUnreachablePlacement_IsNotStored(int left, int top, int width, int height)
    {
        using (var store = Store("Screen 1"))
        {
            store.Schedule(new WindowPlacement(120, 80, 1600, 900, false));
            store.Schedule(new WindowPlacement(left, top, width, height, false));
        }

        // The last good placement survives rather than being overwritten by the bad one.
        Assert.Equal(new WindowPlacement(120, 80, 1600, 900, false), Store("Screen 1").Read());
    }

    /// <summary>Recovers a file written before the guard existed.</summary>
    [Fact]
    public void Read_AnUnreachablePlacementAlreadyOnDisk_IsTreatedAsNothingStored()
    {
        var path = Path.Combine(_root, "cache", "screens", "Screen 1.window.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new WindowPlacement(-32000, -32000, 160, 39, false)));

        // Null opens the window at the default, which is reachable; returning it would restore the
        // window somewhere the host can never click.
        Assert.Null(Store("Screen 1").Read());
    }

    [Fact]
    public void Schedule_ANegativeOriginOnASecondMonitor_IsStillStored()
    {
        using (var store = Store("Screen 1"))
            store.Schedule(new WindowPlacement(-1920, -200, 1920, 1080, false));

        // A monitor left of or above the primary is an ordinary setup, not a minimized window.
        Assert.Equal(-1920, Store("Screen 1").Read()!.Left);
    }

    /// <summary>Two screens on one machine each keep their own window.</summary>
    [Fact]
    public void Schedule_DifferentScreens_DoNotShareAPlacement()
    {
        using (var one = Store("Screen 1"))
            one.Schedule(new WindowPlacement(0, 0, 800, 600, false));

        using (var two = Store("Screen 2"))
            two.Schedule(new WindowPlacement(900, 100, 1920, 1080, false));

        Assert.Equal(800, Store("Screen 1").Read()!.Width);
        Assert.Equal(1920, Store("Screen 2").Read()!.Width);
    }

    /// <summary>A zero-sized window is invisible and can't be dragged back, so it reads unstored.</summary>
    [Theory]
    [InlineData(0, 720)]
    [InlineData(1280, 0)]
    public void Read_StoredWindowHasNoSize_IsIgnored(int width, int height)
    {
        using (var store = Store("Screen 1"))
            store.Schedule(new WindowPlacement(10, 10, width, height, false));

        Assert.Null(Store("Screen 1").Read());
    }

    /// <summary>A half-written or hand-edited file must not stop the screen opening.</summary>
    [Fact]
    public void Read_FileIsNotJson_IsNullRatherThanThrowing()
    {
        var path = Path.Combine(_root, "cache", "screens", "Screen 1.window.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        Assert.Null(Store("Screen 1").Read());
    }

    /// <summary>Only the last position is written; a drag must not leave a file per pixel.</summary>
    [Fact]
    public void Schedule_ManyTimes_KeepsOnlyTheLast()
    {
        using (var store = Store("Screen 1"))
        {
            store.Schedule(new WindowPlacement(1, 1, 100, 100, false));
            store.Schedule(new WindowPlacement(2, 2, 200, 200, false));
            store.Schedule(new WindowPlacement(3, 3, 300, 300, false));
        }

        Assert.Equal(new WindowPlacement(3, 3, 300, 300, false), Store("Screen 1").Read());
    }

    /// <summary>A host names screens, so the name reaches a file path as whatever they typed.</summary>
    [Theory]
    [InlineData("Screen 1", "Screen 1")]
    [InlineData("bar/back", "bar_back")]
    [InlineData("  ", "screen")]
    public void SafeFileName_TurnsAnyScreenNameIntoAFileName(string screenId, string expected)
        => Assert.Equal(expected, WindowPlacementStore.SafeFileName(screenId));

    private WindowPlacementStore Store(string screenId)
        => new(screenId, _root, NullLogger.Instance);
}
