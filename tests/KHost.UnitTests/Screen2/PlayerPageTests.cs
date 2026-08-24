using KHost.Screen2;

namespace KHost.UnitTests.Screen2;

public class PlayerPageTests
{
    [Fact]
    public void BuildPlayerPage_Always_InlinesThePlayerScript()
    {
        var page = Program.BuildPlayerPage();

        // A marker from player.js itself: the page must carry the source, not a reference to it.
        Assert.Contains("window.external.sendMessage", page);
        Assert.DoesNotContain("<script src=\"player.js\"></script>", page);
    }

    [Fact]
    public void BuildPlayerPage_Always_KeepsTheElementsThePlayerDrives()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("id=\"video\"", page);
        Assert.Contains("id=\"background\"", page);
        Assert.Contains("id=\"still\"", page);
        Assert.Contains("id=\"placeholder\"", page);
        Assert.Contains("id=\"blanked\"", page);
        Assert.Contains("id=\"hostlost\"", page);
    }

    // The bed is a second element rather than a second source on the video: it carries no
    // timeline, and sharing the element would put it under the same correction as the song.
    [Fact]
    public void BuildPlayerPage_Always_HandlesTheBackgroundChannelCommands()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("'bg-load'", page);
        Assert.Contains("'bg-play'", page);
        Assert.Contains("'bg-pause'", page);
        Assert.Contains("'bg-stop'", page);
        Assert.Contains("'bg-volume'", page);
        Assert.Contains("type: 'bg-ended'", page);
    }

    [Fact]
    public void BuildPlayerPage_Always_HandlesTheStillCommands()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("'show-image'", page);
        Assert.Contains("'hide-image'", page);
    }

    // A screen is rarely the same shape as the picture, so the host's choice has to reach the
    // element as an object-fit rather than the page assuming one.
    [Fact]
    public void BuildPlayerPage_Always_MapsEveryScalingModeToAnObjectFit()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("objectFit", page);
        Assert.Contains("fit: 'contain'", page);
        Assert.Contains("fill: 'cover'", page);
        Assert.Contains("stretch: 'fill'", page);
        Assert.Contains("original: 'none'", page);
    }
}
