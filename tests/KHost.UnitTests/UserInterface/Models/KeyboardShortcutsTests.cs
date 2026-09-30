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
}
