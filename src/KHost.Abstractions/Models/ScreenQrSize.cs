namespace KHost.Abstractions.Models;

/// <summary>QR size in three steps, not pixels, since only the screen knows its own resolution.</summary>
public enum ScreenQrSize
{
    /// <summary>The smallest of the three steps.</summary>
    Small,

    /// <summary>The middle step, and the default when nothing is chosen.</summary>
    Medium,

    /// <summary>The largest of the three steps.</summary>
    Large,
}
