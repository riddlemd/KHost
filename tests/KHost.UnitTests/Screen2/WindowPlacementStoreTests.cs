using System.Text.Json;
using KHost.Screen2;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Screen2;

/// <summary>
/// A screen comes back where it was left. The store lives on the screen's own machine, so it has
/// to survive a host that closes screens by killing the process — which is why writes are
/// scheduled as the window moves rather than done on the way out.
/// </summary>
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

    /// <summary>Full screen is a flag, not the monitor's pixels — the screen may come back elsewhere.</summary>
    [Fact]
    public void Schedule_FullScreen_RemembersTheFlagAndTheWindowUnderneath()
    {
        using (var store = Store("Screen 1"))
            store.Schedule(new WindowPlacement(10, 20, 1280, 720, true));

        var placement = Store("Screen 1").Read();

        Assert.True(placement!.FullScreen);
        Assert.Equal(1280, placement.Width);
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

    /// <summary>
    /// A zero-sized window is invisible and cannot be dragged back, so a stored one is treated as
    /// nothing stored rather than restored faithfully.
    /// </summary>
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
