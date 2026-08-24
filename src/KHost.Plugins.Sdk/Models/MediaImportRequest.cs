namespace KHost.Plugins.Sdk.Models;

public record MediaImportRequest
{
    public required string FilePath { get; set; }
    public required string Title { get; set; }

    /// <summary>Empty when the source cannot tell the two apart — a video title, say.</summary>
    public string Artist { get; set; } = string.Empty;
    public TimeSpan? Duration { get; set; }
    public string Notes { get; set; } = string.Empty;

    /// <summary>The provider's display name, shown on the host's Downloads page. Empty if unset.</summary>
    public string Source { get; set; } = string.Empty;
}
