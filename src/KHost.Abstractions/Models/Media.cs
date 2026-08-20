namespace KHost.Abstractions.Models;

public enum MediaStatus { Unknown, Ready, Downloading, Processing, Broken }

public class Media : RepositoryModel
{
    public required string FilePath { get; set; }
    public TimeSpan? Duration { get; set; }
    public MediaStatus Status { get; set; }

    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _notes = "";

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

    public string Notes
    {
        get => _notes;
        set { _notes = value; RefoldSearch(); }
    }

    /// <summary>
    /// Title, artist and notes as one folded haystack — composed and lowercased. Search matches any
    /// of the three, so one column answers the query with a single comparison. Refolded whenever any
    /// of them is set, so it cannot drift.
    /// </summary>
    public string SearchFolded { get; private set; } = string.Empty;

    private void RefoldSearch() => SearchFolded = TextFolding.Fold($"{_title} {_artist} {_notes}");

    /// <summary>Size in bytes. Null on rows imported before content dedup, and measured on the next import run.</summary>
    public long? FileSize { get; set; }

    /// <summary>Hash of the size plus the first and last 64 KB — the cheap tier that separates same-size files.</summary>
    public string? SampledHash { get; set; }

    /// <summary>Full SHA-256. Filled in only when a sampled-hash match has to be confirmed.</summary>
    public string? ContentHash { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
