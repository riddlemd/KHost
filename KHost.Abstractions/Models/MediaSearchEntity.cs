namespace KHost.Abstractions.Models;

public record MediaSearchEntity
{
    public required string Source { get; set; }
    public required string ForeignKey { get; set; }
    public required string DisplayName { get; set; }
    public string Notes { get; set; } = string.Empty;
    public IEnumerable<MediaProviderAction> SupportedActions { get; set; } = [];
}
