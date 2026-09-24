namespace KHost.Abstractions.Models.Backgrounds;

/// <summary>One background a venue may draw behind the lyrics.</summary>
/// <remarks>Paths come from enumerating the folder, never from joining a stored name onto it, so
/// nothing here can point outside the pack.</remarks>
public sealed class BackgroundPackEntry
{
    /// <summary>File name with its extension, and what a venue's enabled list holds.</summary>
    /// <remarks>With the extension, because two clips may share a stem: <c>amber.mp4</c> and
    /// <c>amber.mov</c> are different backgrounds with the same name. Stored rather than the
    /// absolute path, so a pack that moves between machines keeps the venue's choices.</remarks>
    public required string File { get; init; }

    /// <summary>The file name without its extension, which is the whole of the naming.</summary>
    public required string Name { get; init; }

    /// <summary>Full path to the clip on disk.</summary>
    public required string FilePath { get; init; }

    /// <summary>The still a venue picks by: the same name with a picture extension, or null when
    /// the pack has none beside the clip.</summary>
    public string? StillPath { get; init; }
}
