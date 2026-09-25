namespace KHost.Abstractions.Models;

/// <summary>Where an overlay sits: a corner, not the edge <see cref="MarqueePosition"/> uses.</summary>
/// <remarks>A venue's saved settings hold this by number, so the values keep their order.</remarks>
public enum OverlayCorner
{
    /// <summary>Bottom-right corner of the picture.</summary>
    BottomRight,

    /// <summary>Bottom-left corner of the picture.</summary>
    BottomLeft,

    /// <summary>Top-right corner of the picture.</summary>
    TopRight,

    /// <summary>Top-left corner of the picture.</summary>
    TopLeft,
}
