namespace KHost.Abstractions.Models;

public enum MediaStatus { Unknown, Ready, Downloading, Processing, Broken }

/// <summary>
/// What the file is for. Karaoke is first so rows written before the column existed read as the
/// songs they are, and so a caller that forgets to set it lands on the harmless kind.
/// </summary>
public enum MediaKind { Karaoke, BreakMusic, Ad }

public class Media : RepositoryModel
{
    public required string FilePath { get; set; }
    public TimeSpan? Duration { get; set; }
    public MediaStatus Status { get; set; }
    public MediaKind Kind { get; set; }

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
