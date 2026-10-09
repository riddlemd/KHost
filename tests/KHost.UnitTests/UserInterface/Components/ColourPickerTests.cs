using Bunit;
using KHost.UserInterface.Components;

namespace KHost.UnitTests.UserInterface.Components;

public class ColourPickerTests : BunitContext
{
    private string? _changedTo = "unchanged";

    private IRenderedComponent<ColourPicker> Picker(string? value, bool clearable = true)
        => Render<ColourPicker>(ps => ps
            .Add(p => p.Id, "colour")
            .Add(p => p.Value, value)
            .Add(p => p.Fallback, "#123456")
            .Add(p => p.Clearable, clearable)
            .Add(p => p.ValueChanged, (string? v) => _changedTo = v));

    [Fact]
    public void NoColour_ShowsTheFallbackWithNothingToClear()
    {
        var cut = Picker(null);

        Assert.Equal("#123456", cut.Find("#colour").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#colour-clear"));
    }

    [Fact]
    public void AColour_ShowsItWithAClearButton()
    {
        var cut = Picker("#abcdef");

        Assert.Equal("#abcdef", cut.Find("#colour").GetAttribute("value"));
        Assert.Single(cut.FindAll("#colour-clear"));
    }

    [Fact]
    public void Clearing_HandsBackNoColour()
    {
        Picker("#abcdef").Find("#colour-clear").Click();

        Assert.Null(_changedTo);
    }

    [Fact]
    public void Picking_HandsBackThePickedColour()
    {
        Picker(null).Find("#colour").Change("#00ff00");

        Assert.Equal("#00ff00", _changedTo);
    }

    /// <summary>A box that must always hold a colour offers no way to empty it.</summary>
    [Fact]
    public void NotClearable_AColour_HasNoClearButton()
        => Assert.Empty(Picker("#abcdef", clearable: false).FindAll("#colour-clear"));
}
