using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

/// <summary>What goes behind a playing song whose words the host draws.</summary>
public class SongBackdropTests
{
    [Fact]
    public void ForPlaying_TimedWordsAndNoPicture_IsBlack()
        => Assert.Equal(SongBackdrop.Black, SongBackdrops.ForPlaying(hasTimedLyrics: true, hasOwnPicture: false));

    [Fact]
    public void ForPlaying_TimedWordsOverTheSongsOwnVideo_KeepsTheVideo()
        => Assert.Equal(SongBackdrop.OwnPicture, SongBackdrops.ForPlaying(hasTimedLyrics: true, hasOwnPicture: true));

    /// <summary>A song with no timed words keeps whatever picture it arrives with.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForPlaying_NoTimedWords_IsNotDecidedHere(bool hasOwnPicture)
        => Assert.Null(SongBackdrops.ForPlaying(hasTimedLyrics: false, hasOwnPicture));
}
