namespace KHost.Abstractions.Models;

/// <summary>
/// Which edge of the screen the marquee rides. Bottom is the zero value on purpose: a venue row
/// saved before the marquee existed reads every missing key as its default, and a band across the
/// bottom is the one that covers least of what is playing.
/// </summary>
public enum MarqueePosition
{
    Bottom,
    Top,
}
