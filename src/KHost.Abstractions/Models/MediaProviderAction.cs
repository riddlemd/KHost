namespace KHost.Abstractions.Models;

public class MediaProviderAction
{
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public List<MediaProviderAction> SubActions { get; set; } = [];

    /// <summary>
    /// Re-runs the search that produced the row once this action finishes. For an action that
    /// changes what searching would return — signing in, say — and not for one that acts on the
    /// row itself, where re-fetching would only cost the host the results they were reading.
    /// </summary>
    public bool RefreshesResults { get; set; }

    public required Func<MediaSearchEntity, Task> PerformAsync { get; set; }
}
