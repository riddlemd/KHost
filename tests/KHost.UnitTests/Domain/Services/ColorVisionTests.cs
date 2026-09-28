using KHost.Abstractions.Models;
using KHost.Domain.Services;
using static KHost.Domain.Services.ColorVision;

namespace KHost.UnitTests.Domain.Services;

/// <summary>The measurements the colour-blind rule stands on, against published reference values.</summary>
public class ColorVisionTests
{
    private static LyricColor Hex(string hex) => new(
        Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

    /// <summary>Sharma, Wu and Dalal (2005), the CIEDE2000 test data: pairs 1, 7, 17 and 25.</summary>
    [Theory]
    [InlineData(50, 2.6772, -79.7751, 50, 0, -82.7485, 2.0425)]
    [InlineData(50, 0, 0, 50, -1, 2, 2.3669)]
    [InlineData(50, 2.5, 0, 73, 25, -18, 27.1492)]
    [InlineData(60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644)]
    public void DeltaE2000_MatchesThePublishedTestData(double l1, double a1, double b1, double l2, double a2, double b2, double expected)
        => Assert.Equal(expected, DeltaE2000((l1, a1, b1), (l2, a2, b2)), 4);

    [Fact]
    public void Contrast_IsTwentyOneForBlackOnWhite_AndOneForAColourOnItself()
    {
        Assert.Equal(21, Contrast(Hex("#000000"), Hex("#FFFFFF")), 6);
        Assert.Equal(21, Contrast(Hex("#FFFFFF"), Hex("#000000")), 6);
        Assert.Equal(1, Contrast(Hex("#8558FA"), Hex("#8558FA")), 6);
    }

    /// <summary>Machado 2009 in linear RGB, as the research script computed it.</summary>
    [Theory]
    [InlineData(nameof(Sight.Normal), "#0B96CA")]
    [InlineData(nameof(Sight.Protanopia), "#7C95CC")]
    [InlineData(nameof(Sight.Deuteranopia), "#6585C9")]
    [InlineData(nameof(Sight.Tritanopia), "#00A4A8")]
    [InlineData(nameof(Sight.Protanomaly), "#6A94CC")]
    [InlineData(nameof(Sight.Deuteranomaly), "#578CCA")]
    public void Simulate_MatchesTheReferenceForEachSight(string sight, string expected)
        => Assert.Equal(Hex(expected), Simulate(Hex("#0B96CA"), Enum.Parse<Sight>(sight)));

    [Fact]
    public void Simulate_TakesRedAndGreenToTheSameYellowsForADeuteranope()
    {
        // The classic confusion: distinct to normal sight, near-identical once simulated.
        var red = Simulate(Hex("#FF0000"), Sight.Deuteranopia);
        var green = Simulate(Hex("#009D00"), Sight.Deuteranopia);

        Assert.True(DeltaE2000(Hex("#FF0000"), Hex("#009D00")) > 70);
        Assert.True(DeltaE2000(red, green) < 6);
    }

    [Fact]
    public void Lab_OfWhite_IsFullLightnessWithNoColour()
    {
        var (l, a, b) = Lab(Hex("#FFFFFF"));

        Assert.Equal(100, l, 2);
        Assert.Equal(0, a, 1);
        Assert.Equal(0, b, 1);
    }

    [Theory]
    [InlineData("#F52C77", 0.8)]
    [InlineData("#0B96CA", 0.5)]
    [InlineData("#FDD7E6", 0.7)]
    public void WithLightness_LandsOnTheLightness_AndKeepsTheHue(string hex, double lightness)
    {
        var color = Hex(hex);

        var moved = WithLightness(color, lightness);

        Assert.Equal(lightness, Lightness(moved), 2);
        // OKLab keeps the hue; CIELAB, measuring it, sees it drift by a degree or two.
        Assert.InRange(HueDegrees(moved) - HueDegrees(color), -3, 3);
    }

    [Fact]
    public void WithLightness_AtFullLightness_IsWhite()
        => Assert.Equal(Hex("#FFFFFF"), WithLightness(Hex("#F52C77"), 1.0));

    private static double HueDegrees(LyricColor color)
    {
        var (_, a, b) = Lab(color);
        return (Math.Atan2(b, a) * 180 / Math.PI + 360) % 360;
    }
}
