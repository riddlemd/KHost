namespace KHost.Abstractions.Models;

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
