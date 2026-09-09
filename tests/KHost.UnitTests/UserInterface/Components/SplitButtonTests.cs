using Bunit;
using KHost.UserInterface.Components;
using Microsoft.AspNetCore.Components;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>
/// A split button with nothing in its menu hides the half that opens it, and that is done in CSS:
/// <c>.kh-split-btn:not(:has(.kh-split-btn__menu button))</c>. A stylesheet is not applied here, so
/// these guard the markup that rule reads instead — the menu being in the document while closed,
/// and holding a button only when the caller supplied one. Both are load-bearing and neither looks
/// it: rendering the menu only while open, which is the obvious way to write it, brings the dead
/// chevron back with every test still green.
/// </summary>
public class SplitButtonTests : BunitContext
{
    private const string MenuSelector = ".kh-split-btn__menu";
    private const string ToggleSelector = ".kh-split-btn__toggle";
    private const string PrimarySelector = ".kh-split-btn__primary";

    public SplitButtonTests()
        // The component imports its positioning module on first render.
        => JSInterop.Mode = JSRuntimeMode.Loose;

    private IRenderedComponent<SplitButton> RenderWith(RenderFragment? buttons)
        => Render<SplitButton>(parameters => parameters
            .Add(p => p.Text, builder => builder.AddContent(0, "Enqueue"))
            .Add(p => p.Buttons, buttons));

    private static RenderFragment Items(params string[] labels) => builder =>
    {
        foreach (var label in labels)
        {
            builder.OpenElement(0, "button");
            builder.AddContent(1, label);
            builder.CloseElement();
        }
    };

    [Fact]
    public void TheMenu_IsInTheDocumentWhileClosed_SoTheToggleCanTellWhetherItHoldsAnything()
    {
        var button = RenderWith(Items("Edit"));

        var menu = button.Find(MenuSelector);

        Assert.True(menu.HasAttribute("hidden"));
        Assert.Single(button.FindAll($"{MenuSelector} button"));
    }

    /// <summary>What "no options" looks like to the CSS: the menu is there, with nothing in it.</summary>
    [Fact]
    public void AMenuWhoseCallerSuppliedNothing_HoldsNoButtons()
    {
        var button = RenderWith(Items());

        Assert.NotNull(button.Find(MenuSelector));
        Assert.Empty(button.FindAll($"{MenuSelector} button"));
    }

    /// <summary>
    /// An empty list and an AuthorizeView that drew nothing are the same thing here — the caller
    /// passing no fragment at all must look no different.
    /// </summary>
    [Fact]
    public void AMenuWithNoFragmentAtAll_HoldsNoButtons()
    {
        var button = RenderWith(buttons: null);

        Assert.NotNull(button.Find(MenuSelector));
        Assert.Empty(button.FindAll($"{MenuSelector} button"));
    }

    [Fact]
    public void TheToggle_Opens_AndClosesTheMenuAgain()
    {
        var button = RenderWith(Items("Edit", "Delete"));

        button.Find(ToggleSelector).Click();
        Assert.False(button.Find(MenuSelector).HasAttribute("hidden"));

        button.Find(ToggleSelector).Click();
        Assert.True(button.Find(MenuSelector).HasAttribute("hidden"));
    }

    /// <summary>
    /// The overlay is what closes the menu by clicking away from it, so unlike the menu it belongs
    /// on screen only while one is open — a permanent one would eat every click on the page.
    /// </summary>
    [Fact]
    public void TheClickAwayOverlay_ExistsOnlyWhileOpen()
    {
        var button = RenderWith(Items("Edit"));

        Assert.Empty(button.FindAll(".kh-split-btn__overlay"));

        button.Find(ToggleSelector).Click();

        Assert.Single(button.FindAll(".kh-split-btn__overlay"));
    }

    /// <summary>The menu holding a caller's items must not stop the button being a button.</summary>
    [Fact]
    public void ThePrimaryHalf_StillRaisesOnClick()
    {
        var clicks = 0;
        var button = Render<SplitButton>(parameters => parameters
            .Add(p => p.Text, builder => builder.AddContent(0, "Enqueue"))
            .Add(p => p.Buttons, Items("Edit"))
            .Add(p => p.OnClick, () => clicks++));

        button.Find(PrimarySelector).Click();

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Disabled_TakesBothHalvesWithIt()
    {
        var button = Render<SplitButton>(parameters => parameters
            .Add(p => p.Text, builder => builder.AddContent(0, "Enqueue"))
            .Add(p => p.Buttons, Items("Edit"))
            .Add(p => p.Disabled, true));

        Assert.True(button.Find(PrimarySelector).HasAttribute("disabled"));
        Assert.True(button.Find(ToggleSelector).HasAttribute("disabled"));
    }

    /// <summary>The reason PrimaryDisabled exists: a row whose main action cannot apply, but whose
    /// menu still offers something worth reaching.</summary>
    [Fact]
    public void PrimaryDisabled_LeavesTheToggleReachable()
    {
        var button = Render<SplitButton>(parameters => parameters
            .Add(p => p.Text, builder => builder.AddContent(0, "Enqueue"))
            .Add(p => p.Buttons, Items("Edit"))
            .Add(p => p.PrimaryDisabled, true));

        Assert.True(button.Find(PrimarySelector).HasAttribute("disabled"));
        Assert.False(button.Find(ToggleSelector).HasAttribute("disabled"));
    }
}
