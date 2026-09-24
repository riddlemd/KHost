using KHost.Abstractions.Models;
using KHost.Common.Media;

namespace KHost.UnitTests.Common.Media;

// The endpoint refuses by format, so this is what stops a song's file being served down an image
// route, and what decides whether break music keeps playing under an ad.
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

    // --- finding the audio half of a pair ---

    [Fact]
    public void FindKaraokeAudio_FindsTheMp3BesideTheGraphics()
    {
        var folder = Directory.CreateTempSubdirectory("khost-pair");

        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "song.cdg"), [1]);
            var audio = Path.Combine(folder.FullName, "song.mp3");
            File.WriteAllBytes(audio, [1]);

            Assert.Equal(audio, MediaFormats.FindKaraokeAudio(Path.Combine(folder.FullName, "song.cdg")));
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>The pairing is a fact about the disk, not about how anyone typed the name. A
    /// case-sensitive filesystem has SONG.CDG and song.mp3 as a pair that an exact-case lookup on a
    /// built name never finds, and the song then plays silent.</summary>
    [Fact]
    public void FindKaraokeAudio_MatchesWithoutRegardToCase()
    {
        var folder = Directory.CreateTempSubdirectory("khost-pair");

        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "SONG.cdg"), [1]);
            var audio = Path.Combine(folder.FullName, "song.MP3");
            File.WriteAllBytes(audio, [1]);

            Assert.Equal(audio, MediaFormats.FindKaraokeAudio(Path.Combine(folder.FullName, "SONG.cdg")));
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>IsKaraokeTrack has always counted any audio file beside a .cdg as the pair's other
    /// half, while the players only ever looked for .mp3 — so a .wav pair was excluded from import
    /// as "part of a pair" and then played silent. One rule now, and it is the broad one.</summary>
    [Fact]
    public void FindKaraokeAudio_TakesAnyAudioExtension_NotOnlyMp3()
    {
        var folder = Directory.CreateTempSubdirectory("khost-pair");

        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "song.cdg"), [1]);
            var audio = Path.Combine(folder.FullName, "song.wav");
            File.WriteAllBytes(audio, [1]);

            Assert.Equal(audio, MediaFormats.FindKaraokeAudio(Path.Combine(folder.FullName, "song.cdg")));
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public void FindKaraokeAudio_AnswersNothing_WhenOnlyTheGraphicsAreThere()
    {
        var folder = Directory.CreateTempSubdirectory("khost-pair");

        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "song.cdg"), [1]);

            // A .txt beside it is not the other half, and must not be mistaken for it.
            File.WriteAllBytes(Path.Combine(folder.FullName, "song.txt"), [1]);

            Assert.Null(MediaFormats.FindKaraokeAudio(Path.Combine(folder.FullName, "song.cdg")));
        }
        finally { folder.Delete(recursive: true); }
    }

    // --- finding the graphics half of a pair (the mirror direction) ---

    /// <summary>IsKaraokeTrack and WithoutPairedAudio each used to run their own exact-case
    /// File.Exists(ChangeExtension(...)) check instead of this one. Two copies of the same rule
    /// disagree eventually; there is now exactly one, and both call through it.</summary>
    [Fact]
    public void FindKaraokeGraphics_FindsTheCdgBesideTheAudio()
    {
        var folder = Directory.CreateTempSubdirectory("khost-pair");

        try
        {
            var graphics = Path.Combine(folder.FullName, "song.cdg");
            File.WriteAllBytes(graphics, [1]);
            File.WriteAllBytes(Path.Combine(folder.FullName, "song.mp3"), [1]);

            Assert.Equal(graphics, MediaFormats.FindKaraokeGraphics(Path.Combine(folder.FullName, "song.mp3")));
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>Matched by directory listing, not by an exact-case lookup on a built name: a
    /// case-sensitive filesystem has SONG.CDG and song.mp3 as a pair that File.Exists(ChangeExtension)
    /// alone never finds.</summary>
    [Fact]
    public void FindKaraokeGraphics_MatchesWithoutRegardToCase()
    {
        var folder = Directory.CreateTempSubdirectory("khost-pair");

        try
        {
            var graphics = Path.Combine(folder.FullName, "SONG.CDG");
            File.WriteAllBytes(graphics, [1]);
            var audio = Path.Combine(folder.FullName, "song.mp3");
            File.WriteAllBytes(audio, [1]);

            Assert.Equal(graphics, MediaFormats.FindKaraokeGraphics(audio));
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public void FindKaraokeGraphics_AnswersNothing_WhenNoGraphicsBesideIt()
    {
        var folder = Directory.CreateTempSubdirectory("khost-pair");

        try
        {
            var audio = Path.Combine(folder.FullName, "record.mp3");
            File.WriteAllBytes(audio, [1]);

            Assert.Null(MediaFormats.FindKaraokeGraphics(audio));
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>The scenario the reviewer named for the fix: a graphics file whose case does not
    /// match the audio's, still recognised as the pair's other half through IsKaraokeTrack.</summary>
    [Fact]
    public void IsKaraokeTrack_AnMp3WithACaseMismatchedCdgBesideIt_IsOne()
    {
        var folder = Directory.CreateTempSubdirectory("khost-cdg-");

        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "SONG.CDG"), [1]);
            var mp3 = Path.Combine(folder.FullName, "song.mp3");
            File.WriteAllBytes(mp3, [1]);

            Assert.True(MediaFormats.IsKaraokeTrack(mp3));
        }
        finally { folder.Delete(recursive: true); }
    }
}
