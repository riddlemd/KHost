namespace KHost.Abstractions.Models;

/// <summary>Extra conditions for a pool search, passed through the searchable options hook.</summary>
public sealed class MediaPoolSearchOptions
{
    /// <summary>Null returns break music and ad playlists together.</summary>
    public PoolPurpose? Purpose { get; set; }

    /// <summary>
    /// When set, narrows to this venue's pools plus the ones scoped to no venue. Null returns
    /// every pool whatever its venue.
    /// </summary>
    public Guid? VenueId { get; set; }
}
