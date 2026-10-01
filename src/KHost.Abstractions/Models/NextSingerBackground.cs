namespace KHost.Abstractions.Models;

/// <summary>What the "Up next" card is drawn over. Over is the zero value, so a venue saved before
/// the setting existed reads as Over with no backfill.</summary>
public enum NextSingerBackground
{
    /// <summary>The venue's own picture stays up, its still or the placeholder, with the card on a
    /// translucent panel over it.</summary>
    Over,

    /// <summary>Solid black behind the card; the venue's picture is hidden while it is up.</summary>
    Blackout,

    /// <summary>An entry from the venue's visualisation playlist plays behind the card, which keeps
    /// its translucent panel. A venue with no playlist, or an empty one, gets <see cref="Over"/>.</summary>
    Visualisation,
}
