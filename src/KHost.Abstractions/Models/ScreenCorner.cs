namespace KHost.Abstractions.Models;

/// <summary>Where an overlay sits: a corner, not the edge <see cref="MarqueePosition"/> uses.</summary>
public enum ScreenCorner
{
    /// <summary>Bottom-right corner of the screen.</summary>
    BottomRight,

    /// <summary>Bottom-left corner of the screen.</summary>
    BottomLeft,

    /// <summary>Top-right corner of the screen.</summary>
    TopRight,

    /// <summary>Top-left corner of the screen.</summary>
    TopLeft,
}
