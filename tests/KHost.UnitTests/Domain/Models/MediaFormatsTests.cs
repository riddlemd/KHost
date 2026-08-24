using KHost.Abstractions.Models;

namespace KHost.UnitTests.Domain.Models;

// The endpoint refuses by format, so this is what stops a song's file being served down an image
// route — and what decides whether break music keeps playing under an ad.
public class MediaFormatsTests
{
    [Theory]
    [InlineData("PNG")]
    [InlineData("jpg")]
    [InlineData("JPEG")]
    [InlineData("gif")]
    [InlineData("WEBP")]
    [InlineData("bmp")]
    public void IsImage_ImageFormats_AreRecognised(string format)
        => Assert.True(MediaFormats.IsImage(format));

    [Theory]
    [InlineData("MP4")]
    [InlineData("mkv")]
    [InlineData("CDG")]
    [InlineData("mp3")]
    public void IsImage_PlayableFormats_AreNot(string format)
        => Assert.False(MediaFormats.IsImage(format));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsImage_NothingAtAll_IsNotAnImage(string? format)
        => Assert.False(MediaFormats.IsImage(format));

    // Media.Format is stored without a dot, but Path.GetExtension hands one over with it.
    [Fact]
    public void IsImage_LeadingDot_IsAccepted()
        => Assert.True(MediaFormats.IsImage(".png"));

    [Fact]
    public void ContentTypeFor_JpgAndJpeg_AgreeOnOneType()
        => Assert.Equal(MediaFormats.ContentTypeFor("JPG"), MediaFormats.ContentTypeFor("JPEG"));

    [Fact]
    public void ContentTypeFor_APlayableFormat_IsNull()
        => Assert.Null(MediaFormats.ContentTypeFor("MP4"));
}
