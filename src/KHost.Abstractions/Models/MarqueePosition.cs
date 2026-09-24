namespace KHost.Abstractions.Models;

/// <summary>Which edge the marquee rides; Bottom is the zero value, so an old row defaults there.</summary>
public enum MarqueePosition
{
    /// <summary>Along the bottom edge of the screen.</summary>
    Bottom,

    /// <summary>Along the top edge of the screen.</summary>
    Top,
}
