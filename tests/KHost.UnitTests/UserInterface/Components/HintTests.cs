using Bunit;
using KHost.UserInterface.Components;
using Microsoft.AspNetCore.Components;

namespace KHost.UnitTests.UserInterface.Components;

public class HintTests : BunitContext
{
    private IRenderedComponent<Hint> RenderHint() => Render<Hint>(p => p
        .Add(h => h.Text, "Deleted when KHost closes.")
        .Add(h => h.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<span class=\"tag\">Ephemeral</span>"))));

    [Fact]
    public void Render_DrawsWhatItExplains_AndTheTextAsATooltipDescribingIt()
    {
        var cut = RenderHint();

        var hint = cut.Find(".kh-hint");
        var text = cut.Find(".kh-hint__text");
        Assert.NotNull(hint.QuerySelector(".tag"));
        Assert.Equal("Deleted when KHost closes.", text.TextContent);
        Assert.Equal("tooltip", text.GetAttribute("role"));
        Assert.Equal(text.Id, hint.GetAttribute("aria-describedby"));
        Assert.Equal("0", hint.GetAttribute("tabindex"));
    }

    /// <summary>A touchscreen has no hover, so a tap is what shows it.</summary>
    [Fact]
    public void Tap_OpensIt_AndASecondTapClosesIt()
    {
        var cut = RenderHint();

        cut.Find(".kh-hint").Click();
        Assert.Contains("kh-hint__text--open", cut.Find(".kh-hint__text").ClassList);

        cut.Find(".kh-hint").Click();
        Assert.DoesNotContain("kh-hint__text--open", cut.Find(".kh-hint__text").ClassList);
    }

    [Fact]
    public void LeavingIt_ClosesIt()
    {
        var cut = RenderHint();
        cut.Find(".kh-hint").Click();

        cut.Find(".kh-hint").FocusOut();

        Assert.DoesNotContain("kh-hint__text--open", cut.Find(".kh-hint__text").ClassList);
    }

    /// <summary>Two hints on one page must not describe each other.</summary>
    [Fact]
    public void TwoHints_HaveTheirOwnIds()
    {
        var first = RenderHint().Find(".kh-hint__text").Id;
        var second = RenderHint().Find(".kh-hint__text").Id;

        Assert.NotEqual(first, second);
    }
}
