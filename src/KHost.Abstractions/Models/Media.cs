namespace KHost.Abstractions.Models;

public enum MediaStatus { Unknown, Ready, Downloading, Processing, Broken }

public class Media : RepositoryModel
{
    public required string FilePath { get; set; }
    public TimeSpan? Duration { get; set; }
    public MediaStatus Status { get; set; }

    private string _title = string.Empty;
    private string _artist = string.Empty;

    public required string Title
    {
        get => _title;
        set { _title = value; RefoldSearch(); }
    }

    public string Artist
    {
        get => _artist;
        set { _artist = value; RefoldSearch(); }
    }

    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// What the host has learned about this file. Deliberately not searchable: notes describe media
    /// already found, so a word buried in one should not pull the song up as a match.
    /// </summary>
    public string Notes { get; set; } = "";

    /// <summary>
    /// Title and artist as one folded haystack — composed and lowercased — holding exactly the text
    /// media_fts indexes, so the short-query fallback finds a song by the same words the index does.
    /// Refolded whenever either is set, so it cannot drift.
    /// </summary>
    public string SearchFolded { get; private set; } = string.Empty;

    private void RefoldSearch() => SearchFolded = TextFolding.Fold($"{_title} {_artist}");

    /// <summary>Size in bytes. Null on rows imported before content dedup, and measured on the next import run.</summary>
    public long? FileSize { get; set; }

    /// <summary>Hash of the size plus the first and last 64 KB — the cheap tier that separates same-size files.</summary>
    public string? SampledHash { get; set; }

    /// <summary>Full SHA-256. Filled in only when a sampled-hash match has to be confirmed.</summary>
    public string? ContentHash { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
