using Bunit;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>No About here — that stays the menu's own page; this is operating instructions only.</summary>
public class HelpDialogTests : BunitContext
{
    private const string SectionSelector = ".kh-help-dialog__section";

    private readonly ControlState _controlState = new();

    public HelpDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IControlState>(_controlState);
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
    public void Body_DoesNotIncludeAbout()
    {
        var dialog = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));

        Assert.DoesNotContain(dialog.FindAll("h3"), h => h.TextContent == "About");
    }

    [Fact]
    public void Sections_QuickGuideComesBeforeShortcutsAndBothHaveContent()
    {
        var dialog = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));

        var sections = dialog.FindAll(SectionSelector);

        Assert.Equal(2, sections.Count);
        Assert.Contains("Quick guide", sections[0].TextContent);
        Assert.Contains("Keyboard shortcuts", sections[1].TextContent);
        Assert.NotEmpty(sections[0].QuerySelectorAll(".kh-help-dialog__guide li"));
        Assert.NotEmpty(sections[1].QuerySelectorAll(".kh-help-dialog__group"));
    }

    [Fact]
    public void Sections_BothCollapsedByDefault()
    {
        var dialog = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));

        Assert.All(dialog.FindAll(SectionSelector), s => Assert.False(s.HasAttribute("open")));
    }

    [Fact]
    public void TogglingTheQuickGuideDetails_ExpandsIt()
    {
        var dialog = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));

        dialog.FindAll(SectionSelector)[0].TriggerEvent("ontoggle", new EventArgs());

        Assert.True(_controlState.HelpQuickGuideExpanded);
        Assert.False(_controlState.HelpShortcutsExpanded);
    }

    [Fact]
    public void TogglingTheShortcutsDetails_ExpandsIt()
    {
        var dialog = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));

        dialog.FindAll(SectionSelector)[1].TriggerEvent("ontoggle", new EventArgs());

        Assert.True(_controlState.HelpShortcutsExpanded);
        Assert.False(_controlState.HelpQuickGuideExpanded);
    }

    /// <summary>A closed-then-reopened dialog is a fresh component (new @key); the state has to live in IControlState, not a field.</summary>
    [Fact]
    public void ExpandedState_SurvivesRenderingTheDialogAgain()
    {
        var first = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));
        first.FindAll(SectionSelector)[0].TriggerEvent("ontoggle", new EventArgs());

        var second = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));
        var sections = second.FindAll(SectionSelector);

        Assert.True(sections[0].HasAttribute("open"));
        Assert.False(sections[1].HasAttribute("open"));
    }
}
