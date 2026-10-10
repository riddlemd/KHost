namespace KHost.Abstractions.Models;

/// <summary>What a caller supplies to bring one file into the library.</summary>
public record MediaImportRequest
{
    /// <summary>Full path to the file on disk.</summary>
    public required string FilePath { get; set; }

    /// <summary>The song's title.</summary>
    public required string Title { get; set; }

    /// <summary>Empty when the source cannot tell the two apart, such as a video title.</summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>How long it plays, if already known.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Free text about the import, shown alongside the row.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>The provider's display name, shown on the host's Downloads page. Empty if unset.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>The provider's own name for the file, such as a video id, handed back if it has to
    /// be fetched again; see <see cref="Media.SourceKey"/>.</summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>See <see cref="Media.IsEphemeral"/>. Taken only on the import that creates the row.</summary>
    public bool IsEphemeral { get; set; }

    /// <summary>See <see cref="Media.IsSingleUse"/>. Taken only on the import that creates the row.</summary>
    public bool IsSingleUse { get; set; }
}
