namespace KHost.Abstractions.Models;

public enum MediaStatus { Unknown, Ready, Downloading, Processing, Broken }

/// <summary>
/// What the file is. Karaoke is first so rows written before the column existed read as the songs
/// they are, and so a caller that forgets to set it lands on the harmless kind.
/// </summary>
/// <remarks>
/// What a file is, not what it is for: an ad is composed in a playlist out of these, so there is
/// no ad kind. Karaoke is the one the queue plays — an mp4 or a cdg+mp3 pair with no singer on it.
/// </remarks>
public enum MediaKind
{
    Karaoke,
    Video,
    Audio,
    Image,
}

/// <summary>
/// How a still fills the screen. Screens are not all the same shape as the picture, so the host
/// picks per image rather than the app guessing: a wide banner and a portrait poster want
/// opposite answers on the same television.
/// </summary>
public enum ImageScaling
{
    /// <summary>Whole picture visible, bars where the shapes disagree.</summary>
    Fit,

    /// <summary>Fills the screen, cropping whatever hangs over the edges.</summary>
    Fill,

    /// <summary>Fills the screen by distorting the picture to match it.</summary>
    Stretch,

    /// <summary>Native pixels, centred. Crops if larger than the screen, bars if smaller.</summary>
    Original,
}

public class Media : RepositoryModel
{
    public required string FilePath { get; set; }
    public TimeSpan? Duration { get; set; }
    public MediaStatus Status { get; set; }
    public MediaKind Kind { get; set; }

    /// <summary>Read only for a still. Fit is the safe answer: nothing is cropped or distorted.</summary>
    public ImageScaling ImageScaling { get; set; }

    public required string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// What the host has learned about this file. Deliberately not searchable: notes describe media
    /// already found, so a word buried in one should not pull the song up as a match.
    /// </summary>
    public string Notes { get; set; } = "";

    /// <summary>
    /// Title and artist as one folded haystack, holding exactly the text media_fts indexes so the
    /// short-query fallback finds a song by the same words the index does. Written by the
    /// persistence layer, not by hand.
    /// </summary>
    public string SearchFolded { get; set; } = string.Empty;

    /// <summary>Size in bytes. Null on rows imported before content dedup, and measured on the next import run.</summary>
    public long? FileSize { get; set; }

    /// <summary>Hash of the size plus the first and last 64 KB — the cheap tier that separates same-size files.</summary>
    public string? SampledHash { get; set; }

    /// <summary>Full SHA-256. Filled in only when a sampled-hash match has to be confirmed.</summary>
    public string? ContentHash { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
