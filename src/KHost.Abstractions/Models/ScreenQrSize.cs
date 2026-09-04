namespace KHost.Abstractions.Models;

/// <summary>
/// How big a QR code is drawn. Three sizes rather than pixels, because the screen is the only
/// thing that knows its own resolution and the code has to be scannable from where the room is
/// standing — unlike the marquee's font size, which is a matter of taste.
/// </summary>
public enum ScreenQrSize
{
    Small,
    Medium,
    Large,
}
