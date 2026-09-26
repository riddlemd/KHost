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
}
