using KHost.UserInterface.Models;

namespace KHost.UnitTests.UserInterface.Models;

/// <summary>The dialog is generated from this list, so a shortcut missing here is invisible.</summary>
public class KeyboardShortcutsTests
{
    [Fact]
    public void All_ListsTheHelpChord()
    {
        var keys = KeyboardShortcuts.All
            .SelectMany(group => group.Shortcuts)
            .Select(shortcut => shortcut.Keys);

        Assert.Contains(keys, k => k.Length == 1 && k[0] == "?");
    }

    [Fact]
    public void All_ListsThePlayNextChord()
    {
        var keys = KeyboardShortcuts.All
            .SelectMany(group => group.Shortcuts)
            .Select(shortcut => shortcut.Keys);

        Assert.Contains(keys, k => k.Length == 2 && k[0] == "ctrl" && k[1] == "enter");
    }

    [Fact]
    public void All_ListsTheAnnounceNextSingerChord()
    {
        var keys = KeyboardShortcuts.All
            .SelectMany(group => group.Shortcuts)
            .Select(shortcut => shortcut.Keys);

        Assert.Contains(keys, k => k.Length == 3 && k[0] == "ctrl" && k[1] == "shift" && k[2] == "enter");
    }
    [Theory]
    [InlineData("ctrl", "3")]
    [InlineData("ctrl", "4")]
    [InlineData("ctrl", "alt", "enter")]
    [InlineData("ctrl", "backspace")]
    [InlineData("alt", "←")]
    [InlineData("alt", "→")]
    public void All_ListsTheChord(params string[] chord)
        => Assert.Contains(AllKeys(), keys => keys.SequenceEqual(chord));

    [Theory]
    [InlineData("Singer queue")]
    [InlineData("Singer's songs")]
    public void All_ListsRemoveForEachQueue(string group)
        => Assert.Contains(
            KeyboardShortcuts.All.Single(g => g.Title == group).Shortcuts,
            shortcut => shortcut.Keys.SequenceEqual(["delete"]));

    [Fact]
    public void All_ListsTheSearchResultKeys()
    {
        var search = KeyboardShortcuts.All.Single(g => g.Title == "Song search").Shortcuts;

        Assert.Contains(search, shortcut => shortcut.Keys.SequenceEqual(["↓"]));
        Assert.Contains(search, shortcut => shortcut.Keys.SequenceEqual(["↑"]));
        Assert.Contains(search, shortcut => shortcut.Keys.SequenceEqual(["enter"]));
    }

    private static IEnumerable<string[]> AllKeys()
        => KeyboardShortcuts.All.SelectMany(group => group.Shortcuts).Select(shortcut => shortcut.Keys);
}
