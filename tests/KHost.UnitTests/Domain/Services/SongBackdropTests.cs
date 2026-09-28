using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

/// <summary>What goes behind a playing song with timed words.</summary>
public class SongBackdropTests
{
    [Fact]
    public void ForPlaying_TimedWordsAndNoPicture_IsBlack()
        => Assert.Equal(SongBackdrop.Black, SongBackdrops.ForPlaying(true, "/songs/a.mp4", hasMovingPicture: false));

    [Fact]
    public void ForPlaying_TimedWordsOverTheSongsOwnVideo_KeepsTheVideo()
        => Assert.Equal(SongBackdrop.OwnPicture, SongBackdrops.ForPlaying(true, "/songs/a.mp4", hasMovingPicture: true));

    /// <summary>An audio file is never a picture, whatever a probe finds inside it.</summary>
    [Theory]
    [InlineData("/songs/a.mp3")]
    [InlineData("/songs/a.M4A")]
    [InlineData("/songs/a.flac")]
    public void ForPlaying_TimedWordsOverAnAudioFile_IsBlackEvenWithAPictureInIt(string source)
        => Assert.Equal(SongBackdrop.Black, SongBackdrops.ForPlaying(true, source, hasMovingPicture: true));

    [Theory]
    [InlineData("/songs/a.mp3", false)]
    [InlineData("/songs/a.Mp3", false)]
    [InlineData("/songs/a.mp4", true)]
    [InlineData("/songs/a.mka", true)]
    public void MayShowPictureFrom_AnswersByWhetherTheFileIsAudio(string source, bool expected)
        => Assert.Equal(expected, SongBackdrops.MayShowPictureFrom(source));

    /// <summary>A song with no timed words keeps whatever picture it arrives with.</summary>
    [Theory]
    [InlineData("/songs/a.mp4", false)]
    [InlineData("/songs/a.mp4", true)]
    [InlineData("/songs/a.mp3", true)]
    public void ForPlaying_NoTimedWords_IsNotDecidedHere(string source, bool hasMovingPicture)
        => Assert.Null(SongBackdrops.ForPlaying(false, source, hasMovingPicture));
}
