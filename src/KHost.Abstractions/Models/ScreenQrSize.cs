namespace KHost.Abstractions.Models;

/// <summary>QR size in three steps, not pixels, since only the screen knows its own resolution.</summary>
public enum ScreenQrSize
{
    Small,
    Medium,
    Large,
}
