namespace KHost.Plugins.Sdk.Models;

public record MediaSearchEntity
{
    public required string SourceDisplayName { get; set; }
    public required string Source { get; set; }
    public required string ForeignKey { get; set; }
    public required string DisplayName { get; set; }
    public TimeSpan? Duration { get; set; }
    public string Notes { get; set; } = string.Empty;
    public IEnumerable<MediaProviderAction> SupportedActions { get; set; } = [];
}
