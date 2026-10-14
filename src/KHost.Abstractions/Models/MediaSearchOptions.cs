namespace KHost.Abstractions.Models;

/// <summary>Which types and statuses a media read or search includes, passed to
/// <see cref="Services.IMediaService"/>'s typed overloads.</summary>
public sealed class MediaSearchOptions
{
    /// <summary>Defaults to karaoke, not every type, so a forgotten arg can't offer up an ad.</summary>
    public MediaType[]? Types { get; set; } = [MediaType.Karaoke];

    /// <summary>Restricts results to these statuses. Null returns every status.</summary>
    public HashSet<MediaStatus>? Statuses { get; set; }

    /// <summary>What every read gets when the caller supplies nothing.</summary>
    public static MediaSearchOptions Default { get; } = new();

    /// <summary>Every type, for the pages that manage media rather than play them.</summary>
    public static MediaSearchOptions AllTypes { get; } = new() { Types = null };
}
