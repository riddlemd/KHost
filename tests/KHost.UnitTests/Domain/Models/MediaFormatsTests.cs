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

    // A backing track has no singer on it and is often not the original recording, so it is a song
    // to queue and nothing else. The .cdg is what gives the pair away.
    [Fact]
    public void IsKaraokeTrack_ACdg_IsOne()
    {
        var dir = Directory.CreateTempSubdirectory("khost-cdg-");
        try
        {
            var cdg = Path.Combine(dir.FullName, "song.cdg");
            File.WriteAllText(cdg, "");

            Assert.True(MediaFormats.IsKaraokeTrack(cdg));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void IsKaraokeTrack_AnMp3WithACdgBesideIt_IsOne()
    {
        var dir = Directory.CreateTempSubdirectory("khost-cdg-");
        try
        {
            var mp3 = Path.Combine(dir.FullName, "song.mp3");
            File.WriteAllText(mp3, "");
            File.WriteAllText(Path.Combine(dir.FullName, "song.cdg"), "");

            Assert.True(MediaFormats.IsKaraokeTrack(mp3));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void IsKaraokeTrack_AnMp3OnItsOwn_IsNot()
    {
        var dir = Directory.CreateTempSubdirectory("khost-cdg-");
        try
        {
            var mp3 = Path.Combine(dir.FullName, "record.mp3");
            File.WriteAllText(mp3, "");

            Assert.False(MediaFormats.IsKaraokeTrack(mp3));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void IsKaraokeTrack_NoPath_IsNot()
        => Assert.False(MediaFormats.IsKaraokeTrack(""));
}
