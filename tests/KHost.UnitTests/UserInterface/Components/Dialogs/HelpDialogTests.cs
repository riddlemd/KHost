using Bunit;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>No About here — that stays the menu's own page; this is operating instructions only.</summary>
public class HelpDialogTests : BunitContext
{
    public HelpDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Shortcuts_AreGeneratedFromKeyboardShortcutsAll()
    {
        var dialog = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));

        var expectedGroup = KeyboardShortcuts.All[0];
        var expectedShortcut = expectedGroup.Shortcuts[0];

        Assert.Contains(dialog.FindAll(".kh-help-dialog__group-title"), g => g.TextContent == expectedGroup.Title);
        Assert.Contains(dialog.FindAll(".kh-help-dialog__description"), d => d.TextContent == expectedShortcut.Description);
    }

    [Fact]
    public void Body_IncludesTheQuickGuide()
    {
        var dialog = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));

        Assert.Contains(dialog.FindAll("h3"), h => h.TextContent == "Quick guide");
        Assert.NotEmpty(dialog.FindAll(".kh-help-dialog__guide li"));
    }

    [Fact]
    public void Body_DoesNotIncludeAbout()
    {
        var dialog = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));

        Assert.DoesNotContain(dialog.FindAll("h3"), h => h.TextContent == "About");
    }
}
