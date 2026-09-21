namespace KHost.Abstractions.Models;

/// <summary>A contract with providers outside this repo: changing it breaks their build.</summary>
/// <remarks>Title and Artist stay apart because the library stores them apart, and rejoining them
/// means re-parsing a string the console built. ForeignKey is the provider's own key; only a
/// local result's is already a library id, so a remote result is imported before it can be
/// enqueued (Performance.MediaId is a Guid into the library).</remarks>
public record MediaSearchEntity
{
    public required string SourceDisplayName { get; set; }
    public required string Source { get; set; }
    public required string ForeignKey { get; set; }
    public required string Title { get; set; }

    /// <summary>Empty when the source cannot tell the two apart, such as a video title.</summary>
    public string Artist { get; set; } = string.Empty;
    public TimeSpan? Duration { get; set; }
    public string Notes { get; set; } = string.Empty;

    /// <summary>Draws this row as one cell across the table; it is for an offer, not a result.</summary>
    public bool SpansAllColumns { get; set; }

    /// <summary>Values keyed by <see cref="MediaResultColumn.Key"/>; not title/artist/duration.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();

    public IEnumerable<MediaProviderAction> SupportedActions { get; set; } = [];
}
