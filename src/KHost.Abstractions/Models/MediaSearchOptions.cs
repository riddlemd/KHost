namespace KHost.Abstractions.Models;

/// <summary>Extra conditions for a media search, passed through the searchable options hook.</summary>
public sealed class MediaSearchOptions
{
    /// <summary>Defaults to karaoke, not every type, so a forgotten arg can't offer up an ad.</summary>
    public MediaType[]? Types { get; set; } = [MediaType.Karaoke];

    public HashSet<MediaStatus>? Statuses { get; set; }

    /// <summary>What every read gets when the caller supplies nothing.</summary>
    public static MediaSearchOptions Default { get; } = new();

    /// <summary>Every type, for the pages that manage media rather than play them.</summary>
    public static MediaSearchOptions AllTypes { get; } = new() { Types = null };
}
