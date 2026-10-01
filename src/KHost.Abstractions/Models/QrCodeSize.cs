namespace KHost.Abstractions.Models;

/// <summary>QR size in four steps, not pixels, since only the display knows its own resolution.</summary>
/// <remarks>A venue's saved settings hold this by number, so the values keep their order; a new
/// step is appended, never inserted.</remarks>
public enum QrCodeSize
{
    /// <summary>The smallest of the four steps.</summary>
    Small,

    /// <summary>The default when nothing is chosen.</summary>
    Medium,

    /// <summary>Larger than <see cref="Medium"/>.</summary>
    Large,

    /// <summary>The largest of the four steps, for a room that needs the code readable from across it.</summary>
    ExtraLarge,
}
