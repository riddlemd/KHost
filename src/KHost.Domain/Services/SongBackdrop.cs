using KHost.Common.Media;

namespace KHost.Domain.Services;

/// <summary>What a playing song's timed words are drawn over.</summary>
public enum SongBackdrop
{
    /// <summary>The song's own moving picture. A cover image is not one, and an audio file never
    /// has one.</summary>
    OwnPicture,

    /// <summary>Plain black.</summary>
    Black,
}

/// <summary>The one rule for what goes behind a playing song with timed words, asked by every path
/// that puts such a song on a display.</summary>
public static class SongBackdrops
{
    /// <summary>What goes under a playing song's timed words, or null for a song with none, whose
    /// picture is whatever it arrives with.</summary>
    /// <param name="hasTimedLyrics">Whether the song has timed words.</param>
    /// <param name="sourcePath">The file the song plays from.</param>
    /// <param name="hasMovingPicture">Whether that file carries a video stream that is not an attached
    /// picture (cover art).</param>
    /// <remarks>Never the venue's card, which is for when nothing is playing, and never its song
    /// backgrounds. A visualiser, when there is one, is the answer here in place of black.</remarks>
    public static SongBackdrop? ForPlaying(bool hasTimedLyrics, string sourcePath, bool hasMovingPicture)
        => !hasTimedLyrics ? null
            : hasMovingPicture && MayShowPictureFrom(sourcePath) ? SongBackdrop.OwnPicture
            : SongBackdrop.Black;

    /// <summary>Whether a song with timed words may show any picture from <paramref name="sourcePath"/>:
    /// never from an audio file, whatever it carries.</summary>
    public static bool MayShowPictureFrom(string sourcePath)
        => !MediaFormats.AudioExtensions.Contains(Path.GetExtension(sourcePath), StringComparer.OrdinalIgnoreCase);
}
