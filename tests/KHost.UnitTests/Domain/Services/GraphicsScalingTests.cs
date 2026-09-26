using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class GraphicsScalingTests
{
    [Fact]
    public void DefaultHeight_IsOff() => Assert.Equal(GraphicsScaling.Off, GraphicsScaling.DefaultHeight);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(720, 720)]
    [InlineData(1080, 1080)]
    [InlineData(2160, 2160)]
    [InlineData(900, 720)]
    [InlineData(719, 0)]
    [InlineData(4320, 2160)]
    [InlineData(-5, 0)]
    public void SnapToOffered_TakesTheLargestOfferedHeightNotAboveIt(int height, int expected)
        => Assert.Equal(expected, GraphicsScaling.SnapToOffered(height));

    [Theory]
    [InlineData(1280, 720, 3)]
    [InlineData(1920, 1080, 5)]
    [InlineData(3840, 2160, 10)]
    [InlineData(200, 100, 1)]
    public void WholeScaleFor_IsTheLargestWholeMultipleThatFits(int width, int height, int expected)
        => Assert.Equal(expected, GraphicsScaling.WholeScaleFor(width, height));

    [Theory]
    [InlineData(720, 1000)]
    [InlineData(1080, 1500)]
    [InlineData(2160, 3000)]
    [InlineData(700, 972)]
    [InlineData(11, 16)]
    public void PictureOfHeight_KeepsTheNativeShape_OnAnEvenWidth(int height, int width)
        => Assert.Equal((width, height), GraphicsScaling.PictureOfHeight(height));

    [Fact]
    public void FrameOfHeight_Is16By9() => Assert.Equal((1920, 1080), GraphicsScaling.FrameOfHeight(1080));
}
