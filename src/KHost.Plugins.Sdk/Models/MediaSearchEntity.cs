namespace KHost.Plugins.Sdk.Models;

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
    /// Values for the provider's own columns, keyed by <see cref="MediaResultColumn.Key"/>. Title,
    /// artist and duration are read from the properties above and do not belong here. A key the
    /// declared columns do not name is ignored, and a column with no value renders empty.
    /// </summary>
    public IReadOnlyDictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();

    public IEnumerable<MediaProviderAction> SupportedActions { get; set; } = [];
}
