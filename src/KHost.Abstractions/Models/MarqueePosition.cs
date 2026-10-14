namespace KHost.Abstractions.Models;

/// <summary>Which edge the marquee rides; Bottom is the zero value, so an old row defaults there.</summary>
public enum MarqueePosition
{
    /// <summary>Along the bottom edge of the screen.</summary>
    Bottom = 0,

    /// <summary>Along the top edge of the screen.</summary>
    Top = 1,
}
