using KHost.Abstractions.Models;
using KHost.Common.Visualisations;

namespace KHost.UnitTests.Common.Visualisations;

/// <summary>One set of range rules for a playlist entry and a performance's own background, each
/// rule asserted on both.</summary>
public class VisualisationLooksTests
{
    public static TheoryData<string> Shapes => ["entry", "background"];

    private static IVisualisationLook New(string shape)
        => shape == "entry" ? new VisualisationEntry() : new PerformanceBackground { Type = PerformanceBackgroundType.Look };

    private static IVisualisationLook Held(string shape, Action<IVisualisationLook> arrange)
    {
        var source = New(shape);
        arrange(source);
        var target = New(shape);
        source.CopyWithinRangesTo(target);
        return target;
    }

    [Theory, MemberData(nameof(Shapes))]
    public void CopyWithinRangesTo_AVideo_KeepsItsVideoAndDropsAnyPresetName(string shape)
    {
        var id = Guid.NewGuid();

        var held = Held(shape, l => { l.PresetSource = VisualiserPresetSource.Video; l.PresetName = "left over"; l.VideoMediaId = id; });

        Assert.Equal((VisualiserPresetSource.Video, "", (Guid?)id), (held.PresetSource, held.PresetName, held.VideoMediaId));
    }

    [Theory, MemberData(nameof(Shapes))]
    public void CopyWithinRangesTo_APreset_KeepsItsNameAndDropsAnyVideo(string shape)
    {
        var held = Held(shape, l => { l.PresetSource = VisualiserPresetSource.Imported; l.PresetName = "My Swirl"; l.VideoMediaId = Guid.NewGuid(); });

        Assert.Equal((VisualiserPresetSource.Imported, "My Swirl", (Guid?)null), (held.PresetSource, held.PresetName, held.VideoMediaId));
    }

    [Theory, MemberData(nameof(Shapes))]
    public void CopyWithinRangesTo_PercentagesBelowTheirRange_AreRaisedToTheFloor(string shape)
    {
        var held = Held(shape, l => { l.Brightness = -1; l.Saturation = -1; l.Sensitivity = -1; });

        Assert.Equal((VisualisationEntry.MinBrightness, VisualisationEntry.MinSaturation, VisualisationEntry.MinSensitivity),
            (held.Brightness, held.Saturation, held.Sensitivity));
    }

    [Theory, MemberData(nameof(Shapes))]
    public void CopyWithinRangesTo_PercentagesAboveTheirRange_AreLoweredToTheCeiling(string shape)
    {
        var held = Held(shape, l => { l.Brightness = 999; l.Saturation = 999; l.Sensitivity = 999; });

        Assert.Equal((VisualisationEntry.MaxBrightness, VisualisationEntry.MaxSaturation, VisualisationEntry.MaxSensitivity),
            (held.Brightness, held.Saturation, held.Sensitivity));
    }

    [Theory, MemberData(nameof(Shapes))]
    public void CopyWithinRangesTo_PercentagesInsideTheirRange_AreKept(string shape)
    {
        var held = Held(shape, l => { l.Brightness = 150; l.Saturation = 40; l.Sensitivity = 220; });

        Assert.Equal((150, 40, 220), (held.Brightness, held.Saturation, held.Sensitivity));
    }

    [Theory]
    [InlineData("entry", 20, 16)]
    [InlineData("background", 20, 16)]
    [InlineData("entry", 40, 32)]
    [InlineData("background", 40, 32)]
    [InlineData("entry", 50, 64)]
    [InlineData("background", 50, 64)]
    public void CopyWithinRangesTo_ABarCountNotOnOffer_TakesTheNearest(string shape, int asked, int held)
        => Assert.Equal(held, Held(shape, l => l.BarCount = asked).BarCount);

    [Theory, MemberData(nameof(Shapes))]
    public void CopyWithinRangesTo_AnUnknownPalette_IsClassic(string shape)
        => Assert.Equal(VisualiserColourScheme.Classic, Held(shape, l => l.ColourScheme = (VisualiserColourScheme)42).ColourScheme);

    [Theory, MemberData(nameof(Shapes))]
    public void CopyWithinRangesTo_AKnownPalette_IsKept(string shape)
        => Assert.Equal(VisualiserColourScheme.Single, Held(shape, l => l.ColourScheme = VisualiserColourScheme.Single).ColourScheme);

    [Theory, MemberData(nameof(Shapes))]
    public void CopyWithinRangesTo_AColour_IsLowercased(string shape)
        => Assert.Equal("#ab12cd", Held(shape, l => l.Colour = "#AB12CD").Colour);

    [Theory]
    [InlineData("entry", "#12345")]
    [InlineData("background", "#12345")]
    [InlineData("entry", "1234567")]
    [InlineData("background", "1234567")]
    [InlineData("entry", "#gg0000")]
    [InlineData("background", "#gg0000")]
    public void CopyWithinRangesTo_AColourNotHex_IsTheDefault(string shape, string colour)
        => Assert.Equal(VisualisationEntry.DefaultColour, Held(shape, l => l.Colour = colour).Colour);

    [Fact]
    public void BackgroundWithinRanges_Null_IsNull()
        => Assert.Null(VisualisationLooks.BackgroundWithinRanges(null));

    [Fact]
    public void BackgroundWithinRanges_AnUnknownType_IsNull()
        => Assert.Null(VisualisationLooks.BackgroundWithinRanges(new PerformanceBackground { Type = (PerformanceBackgroundType)7 }));

    /// <summary>Black draws nothing, so whatever look it carried is not kept to mislead a reader.</summary>
    [Fact]
    public void BackgroundWithinRanges_Black_KeepsBlackWithEveryLookSettingAtItsDefault()
    {
        var held = VisualisationLooks.BackgroundWithinRanges(new PerformanceBackground
        {
            Type = PerformanceBackgroundType.Black,
            PresetSource = VisualiserPresetSource.Imported,
            PresetName = "My Swirl",
            Brightness = 150,
            Colour = "#ff0000",
        })!;

        var defaults = new PerformanceBackground();
        Assert.Equal(PerformanceBackgroundType.Black, held.Type);
        Assert.Equal((defaults.PresetSource, defaults.PresetName, defaults.Brightness, defaults.Colour),
            (held.PresetSource, held.PresetName, held.Brightness, held.Colour));
    }

    [Fact]
    public void BackgroundWithinRanges_ALook_IsACopyHeldToTheRanges()
    {
        var look = new PerformanceBackground
        {
            Type = PerformanceBackgroundType.Look,
            PresetSource = VisualiserPresetSource.BuiltIn,
            PresetName = "spectrum-bars",
            Brightness = 999,
            BarCount = 60,
        };

        var held = VisualisationLooks.BackgroundWithinRanges(look)!;

        Assert.NotSame(look, held);
        Assert.Equal((PerformanceBackgroundType.Look, VisualiserPresetSource.BuiltIn, "spectrum-bars", VisualisationEntry.MaxBrightness, 64),
            (held.Type, held.PresetSource, held.PresetName, held.Brightness, held.BarCount));
    }
}
