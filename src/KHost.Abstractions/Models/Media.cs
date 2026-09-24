namespace KHost.Abstractions.Models;

/// <summary>Whether a library row can be played.</summary>
public enum MediaStatus
{
    /// <summary>Not yet checked.</summary>
    Unknown,

    /// <summary>Playable.</summary>
    Ready,

    /// <summary>Bytes are still arriving; not yet playable.</summary>
    Downloading,

    /// <summary>Bytes are in; a provider is still turning them into something playable.</summary>
    Processing,

    /// <summary>Cannot be played, and won't become playable without a host fixing or re-importing it.</summary>
    Broken,
}

/// <summary>What the file is, not what it is for; an ad is composed in a playlist out of these.</summary>
/// <remarks>Karaoke is first so a caller that forgets to set it lands on the harmless one.</remarks>
public enum MediaType
{
    /// <summary>Words shown for the singer to follow, over music or video.</summary>
    Karaoke,

    /// <summary>Video with nothing for a singer to follow, e.g. break music or an ad clip.</summary>
    Video,

    /// <summary>Sound with no picture of its own.</summary>
    Audio,

    /// <summary>A still picture.</summary>
    Image,
}

/// <summary>How a still fills the screen; the host picks per image, not the app.</summary>
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

/// <summary>One row in the library: a playable file and what is known about it.</summary>
public class Media : RepositoryModel
{
    /// <summary>Full path to the file on disk.</summary>
    public required string FilePath { get; set; }

    /// <summary>How long it plays. Null when not yet known.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Whether this row can be played right now.</summary>
    public MediaStatus Status { get; set; }

    /// <summary>What kind of file this is.</summary>
    public MediaType Type { get; set; }

    /// <summary>Read only for a still. Fit is the safe answer: nothing is cropped or distorted.</summary>
    public ImageScaling ImageScaling { get; set; }

    /// <summary>The song's title.</summary>
    public required string Title { get; set; } = string.Empty;

    /// <summary>The performing artist. Empty when the source cannot tell the two apart.</summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>The file's format or container, for display.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>Provider that produced this file, self-named on import; empty for a scan find.</summary>
    /// <remarks>Unchecked claim: never gate on it; kept out of <see cref="SearchFolded"/>.</remarks>
    public string Source { get; set; } = string.Empty;

    /// <summary>What the host has learned about this file, deliberately not searchable.</summary>
    public string Notes { get; set; } = "";

    /// <summary>Combined, match-insensitive form of <see cref="Title"/> and <see cref="Artist"/> used
    /// for search; the host keeps this in sync, so a plugin should treat it as read-only.</summary>
    public string SearchFolded { get; set; } = string.Empty;

    /// <summary>The file's size, in bytes. Null until it has been measured.</summary>
    public long? FileSize { get; set; }

    /// <summary>A cheap fingerprint used to tell apart same-size files before a full comparison; the
    /// host computes and keeps this current, so a plugin should treat it as read-only.</summary>
    public string? SampledHash { get; set; }

    /// <summary>A fingerprint of the whole file, confirming two files are identical; the host
    /// computes and keeps this current, so a plugin should treat it as read-only. Null unless a
    /// <see cref="SampledHash"/> match needed confirming.</summary>
    public string? ContentHash { get; set; }

    /// <summary>When this row was added to the library, UTC.</summary>
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
