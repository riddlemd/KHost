namespace KHost.Abstractions.Models;

/// <summary>QR size in three steps, not pixels, since only the display knows its own resolution.</summary>
/// <remarks>A venue's saved settings hold this by number, so the values keep their order.</remarks>
public enum QrCodeSize
{
    /// <summary>The smallest of the three steps.</summary>
    Small,

    /// <summary>The middle step, and the default when nothing is chosen.</summary>
    Medium,

    /// <summary>The largest of the three steps.</summary>
    Large,
}
