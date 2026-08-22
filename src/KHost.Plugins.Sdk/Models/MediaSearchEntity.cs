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
    public IEnumerable<MediaProviderAction> SupportedActions { get; set; } = [];
}
