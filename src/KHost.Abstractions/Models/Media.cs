namespace KHost.Abstractions.Models;

public enum MediaStatus { Unknown, Ready, Downloading, Processing, Broken }

/// <summary>What the file is, not what it is for; an ad is composed in a playlist out of these.</summary>
/// <remarks>Karaoke is first so a caller that forgets to set it lands on the harmless one.</remarks>
public enum MediaType
{
    Karaoke,
    Video,
    Audio,
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

public class Media : RepositoryModel
{
    public required string FilePath { get; set; }
    public TimeSpan? Duration { get; set; }
    public MediaStatus Status { get; set; }
    public MediaType Type { get; set; }

    /// <summary>Read only for a still. Fit is the safe answer: nothing is cropped or distorted.</summary>
    public ImageScaling ImageScaling { get; set; }

    public required string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    /// <summary>Provider that produced this file, self-named on import; empty for a scan find.</summary>
    /// <remarks>Unchecked claim: never gate on it; kept out of <see cref="SearchFolded"/>.</remarks>
    public string Source { get; set; } = string.Empty;

    /// <summary>What the host has learned about this file, deliberately not searchable.</summary>
    public string Notes { get; set; } = "";

    /// <summary>Title+artist folded into one haystack matching media_fts. Written by persistence.</summary>
    public string SearchFolded { get; set; } = string.Empty;

    /// <summary>Size in bytes. Null before content dedup, measured on the next import.</summary>
    public long? FileSize { get; set; }

    /// <summary>Hash of size plus first/last 64 KB: the cheap tier separating same-size files.</summary>
    public string? SampledHash { get; set; }

    /// <summary>Full SHA-256. Filled in only when a sampled-hash match has to be confirmed.</summary>
    public string? ContentHash { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
