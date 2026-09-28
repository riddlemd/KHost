namespace KHost.Domain.Services;

/// <summary>What a playing song's timed words are drawn over.</summary>
public enum SongBackdrop
{
    /// <summary>The song's own moving picture. A cover image is not one.</summary>
    OwnPicture,

    /// <summary>Plain black.</summary>
    Black,
}

/// <summary>The one rule for what goes behind a playing song whose words the host draws, asked by
/// every path that puts such a song on a display.</summary>
public static class SongBackdrops
{
    /// <summary>What goes under a playing song's timed words, or null for a song with none, whose
    /// picture is whatever it arrives with.</summary>
    /// <remarks>Never the venue's card, which is for when nothing is playing, and never its song
    /// backgrounds. A visualiser, when there is one, is the answer here in place of black.</remarks>
    public static SongBackdrop? ForPlaying(bool hasTimedLyrics, bool hasOwnPicture)
        => !hasTimedLyrics ? null
            : hasOwnPicture ? SongBackdrop.OwnPicture
            : SongBackdrop.Black;
}
