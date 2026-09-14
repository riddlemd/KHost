using System.Text.RegularExpressions;

namespace KHost.UnitTests.UserInterface;

/// <summary>
/// Every service that answers a screen connecting has to be resolved before the hub is mapped.
/// They subscribe in their constructors, so one nobody has asked for has subscribed to nothing —
/// the first screen of the night then connects to a service that does not exist yet.
/// </summary>
/// <remarks>
/// This is asserted against the startup source rather than by standing an app up, because the
/// failure is a service quietly not being there: a test that built the graph would resolve
/// everything by asking for it and prove the opposite of what it set out to.
///
/// PlaybackService is the one that was missed. It owns the venue's placeholder image, so a screen
/// launched before anything had played got no card at all — and the venue setting looked broken
/// rather than unheard.
/// </remarks>
public class StartupScreenServicesTests
{
    private static readonly string Startup = ReadStartup();

    private static string ReadStartup()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        var path = Path.Combine(directory!.FullName, "src", "KHost.UserInterface", "Program.cs");

        Assert.True(File.Exists(path), $"Could not find Program.cs at {path}.");

        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("IScreenCoordinationService")]
    [InlineData("IScreenMarqueeService")]
    [InlineData("BreakMusicCardService")]
    [InlineData("IPlaybackService")]
    public void EveryServiceThatAnswersAScreen_IsResolvedOnTheWayUp(string service)
        => Assert.Matches(new Regex($@"GetRequiredService<{Regex.Escape(service)}>\(\)"), Startup);

    /// <summary>
    /// Before the hub, not after: a screen can connect the moment it is mapped, and a service
    /// resolved a line later has already missed it.
    /// </summary>
    [Fact]
    public void ThoseServices_AreResolvedBeforeTheHubIsMapped()
    {
        var hub = Startup.IndexOf("MapIPCServer", StringComparison.Ordinal);

        Assert.True(hub > 0, "Program.cs no longer maps the IPC server by that name; this test needs rereading.");

        foreach (var service in new[]
                 {
                     "IScreenCoordinationService", "IScreenMarqueeService",
                     "BreakMusicCardService", "IPlaybackService",
                 })
        {
            var resolved = Startup.IndexOf($"GetRequiredService<{service}>()", StringComparison.Ordinal);

            Assert.True(resolved > 0 && resolved < hub,
                $"{service} is resolved after the hub is mapped, so the first screen to connect reaches nobody.");
        }
    }
}
