namespace KHost.Abstractions.Models;

public record MediaSearchEntity
{
    public required string SourceDisplayName { get; set; }
    public required string Source { get; set; }
    public required string ForeignKey { get; set; }
    public required string Title { get; set; }

    /// <summary>Empty when the source cannot tell the two apart — a video title, say.</summary>
    public string Artist { get; set; } = string.Empty;
    public TimeSpan? Duration { get; set; }
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Draws this row as one cell across the whole table instead of a value per column. For a row
    /// that is an offer rather than a result — a sign-in prompt, a "search somewhere else" — where
    /// the columns describe a song it is not, and an action's label has no room in a column sized
    /// for "Enqueue". The console lays it out: title, <see cref="Notes"/>, picture and actions.
    /// </summary>
    public bool SpansAllColumns { get; set; }

    /// <summary>
    /// Values for the provider's own columns, keyed by <see cref="MediaResultColumn.Key"/>. Title,
    /// artist and duration are read from the properties above and do not belong here. A key the
    /// declared columns do not name is ignored, and a column with no value renders empty.
    /// </summary>
    public IReadOnlyDictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();

    public IEnumerable<MediaProviderAction> SupportedActions { get; set; } = [];
}
