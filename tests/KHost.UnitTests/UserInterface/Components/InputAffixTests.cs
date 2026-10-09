using Bunit;
using KHost.UserInterface.Components;

namespace KHost.UnitTests.UserInterface.Components;

public class InputAffixTests : BunitContext
{
    [Fact]
    public void APrefix_SitsBeforeTheInput_InsideOneBox()
    {
        var cut = Render<InputAffix>(ps => ps.Add(p => p.Prefix, "$").AddChildContent("<input id=\"amount\" />"));

        var box = cut.Find(".kh-input-affix");
        Assert.Equal(["kh-input-affix__text", "INPUT"], box.Children.Select(c => c.TagName == "INPUT" ? "INPUT" : c.ClassList[0]));
        Assert.Equal("$", box.QuerySelector(".kh-input-affix__text--prefix")!.TextContent);
        Assert.Empty(cut.FindAll(".kh-input-affix__text--suffix"));
    }

    [Fact]
    public void ASuffix_SitsAfterTheInput_InsideOneBox()
    {
        var cut = Render<InputAffix>(ps => ps.Add(p => p.Suffix, "%").AddChildContent("<input id=\"level\" />"));

        var box = cut.Find(".kh-input-affix");
        Assert.Equal(["INPUT", "kh-input-affix__text"], box.Children.Select(c => c.TagName == "INPUT" ? "INPUT" : c.ClassList[0]));
        Assert.Equal("%", box.QuerySelector(".kh-input-affix__text--suffix")!.TextContent);
        Assert.Empty(cut.FindAll(".kh-input-affix__text--prefix"));
    }

    [Fact]
    public void Both_WrapTheInput()
    {
        var cut = Render<InputAffix>(ps => ps.Add(p => p.Prefix, "$").Add(p => p.Suffix, "USD").AddChildContent("<input />"));

        Assert.Equal(["$", "USD"], cut.FindAll(".kh-input-affix__text").Select(t => t.TextContent));
    }

    /// <summary>The text is decoration; a screen reader hears the input's own label instead.</summary>
    [Fact]
    public void TheText_IsHiddenFromScreenReaders()
    {
        var cut = Render<InputAffix>(ps => ps.Add(p => p.Prefix, "$").Add(p => p.Suffix, "%").AddChildContent("<input />"));

        Assert.All(cut.FindAll(".kh-input-affix__text"), t => Assert.Equal("true", t.GetAttribute("aria-hidden")));
    }

    [Fact]
    public void ACallersClass_IsAddedToTheBox()
        => Assert.Contains("wide", Render<InputAffix>(ps => ps.Add(p => p.Class, "wide").AddChildContent("<input />")).Find(".kh-input-affix").ClassList);
}
