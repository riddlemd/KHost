namespace KHost.Abstractions.Models;

/// <summary>
/// Where an overlay sits on a screen. Separate from <see cref="MarqueePosition"/>, which names the
/// edge a full-width band is fixed to rather than a corner something small can occupy.
/// </summary>
public enum ScreenCorner
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
}
