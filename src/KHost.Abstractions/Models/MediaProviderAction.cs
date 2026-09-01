namespace KHost.Abstractions.Models;

public class MediaProviderAction
{
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public List<MediaProviderAction> SubActions { get; set; } = [];

    public required Func<MediaSearchEntity, Task> PerformAsync { get; set; }
}
