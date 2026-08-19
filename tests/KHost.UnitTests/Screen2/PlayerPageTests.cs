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
        Assert.Contains("id=\"placeholder\"", page);
        Assert.Contains("id=\"blanked\"", page);
        Assert.Contains("id=\"hostlost\"", page);
    }
}
