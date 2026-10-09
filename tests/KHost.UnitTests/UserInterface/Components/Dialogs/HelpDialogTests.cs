using Bunit;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components.Web;
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
    public void Escape_ClosesIt()
    {
        var closed = false;
        var dialog = Render<HelpDialog>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.OnClose, () => closed = true));

        dialog.Find(".kh-dialog").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(closed);
    }

    /// <summary>Esc reaches the dialog only from focus inside it, and with no input there
    /// focusFirstInput takes [autofocus] or nothing, leaving focus on the header's ? button.</summary>
    [Fact]
    public void Opening_PutsFocusInsideTheDialog()
    {
        var dialog = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));

        Assert.NotNull(dialog.Find(".kh-dialog").QuerySelector("[autofocus]"));
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

    /// <summary>
    /// Reproduces TODO 53: a fresh mount with a persisted "expanded" restores open=true on a brand-new
    /// &lt;details&gt;, which a real browser treats as a state change and fires its own toggle for —
    /// exactly the event a plain "reopen the dialog" click delivers here. Left unswallowed, that one
    /// event inverts the just-restored state (and each inversion fires another), which is the flicker
    /// this test guards against.
    /// </summary>
    [Fact]
    public void ReopeningWithBothSectionsExpanded_SwallowsEachMountTimeToggleAndStaysExpanded()
    {
        _controlState.HelpQuickGuideExpanded = true;
        _controlState.HelpShortcutsExpanded = true;

        var dialog = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));
        var sections = dialog.FindAll(SectionSelector);

        sections[0].TriggerEvent("ontoggle", new EventArgs());
        sections[1].TriggerEvent("ontoggle", new EventArgs());

        Assert.True(_controlState.HelpQuickGuideExpanded);
        Assert.True(_controlState.HelpShortcutsExpanded);
        Assert.True(dialog.FindAll(SectionSelector)[0].HasAttribute("open"));
        Assert.True(dialog.FindAll(SectionSelector)[1].HasAttribute("open"));
    }

    /// <summary>The swallow is one-shot: a genuine user click right after reopening still collapses it.</summary>
    [Fact]
    public void AfterTheMountTimeToggleIsSwallowed_ANextToggleCollapsesNormally()
    {
        _controlState.HelpQuickGuideExpanded = true;

        var dialog = Render<HelpDialog>(p => p.Add(d => d.IsOpen, true));
        var section = dialog.FindAll(SectionSelector)[0];

        section.TriggerEvent("ontoggle", new EventArgs()); // swallowed mount-time toggle
        Assert.True(_controlState.HelpQuickGuideExpanded);

        dialog.FindAll(SectionSelector)[0].TriggerEvent("ontoggle", new EventArgs()); // genuine click
        Assert.False(_controlState.HelpQuickGuideExpanded);
    }
}
