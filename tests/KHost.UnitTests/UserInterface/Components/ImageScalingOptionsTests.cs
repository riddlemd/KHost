using Bunit;
using KHost.UserInterface.Components;

namespace KHost.UnitTests.UserInterface.Components;

public class ImageScalingOptionsTests : BunitContext
{
    [Fact]
    public void Default_RendersTheFourScalingOptionsAndNoBlank()
    {
        var cut = Render<ImageScalingOptions>();

        var options = cut.FindAll("option");

        Assert.Equal(4, options.Count);
        Assert.DoesNotContain(options, o => o.GetAttribute("value") == "");
    }

    [Fact]
    public void IncludeUseOwn_AddsTheBlankOptionFirst()
    {
        var cut = Render<ImageScalingOptions>(parameters => parameters
            .Add(p => p.IncludeUseOwn, true));

        var options = cut.FindAll("option");

        Assert.Equal(5, options.Count);
        Assert.Equal("", options[0].GetAttribute("value"));
        Assert.Contains("own", options[0].TextContent);
    }
}
