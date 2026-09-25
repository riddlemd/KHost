namespace KHost.Abstractions.Models;

/// <summary>One singer waiting their turn, and the song they will sing.</summary>
/// <remarks>Read through <see cref="KHost.Abstractions.Services.IUpNextService"/>. Compared by
/// value.</remarks>
public sealed record UpNextEntry
{
    /// <summary>Where this singer stands among those waiting, from 1 for the next up.</summary>
    public required int Position { get; init; }

    /// <summary>The singer as the room should see them: the name they asked to be called for this
    /// song when the venue allows it, otherwise their account name.</summary>
    public required string Singer { get; init; }

    /// <summary>Their next song's title, or null when they have nothing queued; never blank.</summary>
    public string? Title { get; init; }

    /// <summary>Their next song's artist, or null when there is no song or it names none; never
    /// blank.</summary>
    public string? Artist { get; init; }
}
