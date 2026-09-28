using Bunit;
using KHost.UserInterface.Components;

namespace KHost.UnitTests.UserInterface.Components;

public class OverlayCornerOptionsTests : BunitContext
{
    [Fact]
    public void Render_ListsAllFourCornersOnce()
    {
        var cut = Render<OverlayCornerOptions>();

        var values = cut.FindAll("option").Select(o => o.GetAttribute("value")).ToList();

        Assert.Equal(["BottomRight", "BottomLeft", "TopRight", "TopLeft"], values);
    }
}
