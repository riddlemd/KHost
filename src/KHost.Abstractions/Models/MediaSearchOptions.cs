namespace KHost.Abstractions.Models;

/// <summary>Extra conditions for a media search, passed through the searchable options hook.</summary>
public sealed class MediaSearchOptions
{
    /// <summary>
    /// Null or empty returns every type, which only the media manager wants. It defaults to the
    /// songs a host queues, so a path that forgets to pass options cannot offer an ad up as
    /// singable. Several because an ad's picture is a video or a still, searched together.
    /// </summary>
    public MediaType[]? Types { get; set; } = [MediaType.Karaoke];

    public HashSet<MediaStatus>? Statuses { get; set; }

    /// <summary>What every read gets when the caller supplies nothing.</summary>
    public static MediaSearchOptions Default { get; } = new();

    /// <summary>Every type, for the pages that manage media rather than play them.</summary>
    public static MediaSearchOptions AllTypes { get; } = new() { Types = null };
}
