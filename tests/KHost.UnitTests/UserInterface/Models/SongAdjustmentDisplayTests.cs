using KHost.UserInterface.Models;

namespace KHost.UnitTests.UserInterface.Models;

public class SongAdjustmentDisplayTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(3, "+3")]
    [InlineData(-3, "−3")]
    public void FormatPitch_SignsTheValue_UsingTheMinusSignGlyph(int semitones, string expected)
        => Assert.Equal(expected, SongAdjustmentDisplay.FormatPitch(semitones));

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(10, "+10%")]
    [InlineData(-10, "−10%")]
    public void FormatTempo_SignsTheValue_UsingTheMinusSignGlyph(int tempo, string expected)
        => Assert.Equal(expected, SongAdjustmentDisplay.FormatTempo(tempo));
}
