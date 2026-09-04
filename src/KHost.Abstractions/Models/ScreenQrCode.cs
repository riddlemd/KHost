namespace KHost.Abstractions.Models;

/// <summary>
/// A QR code someone wants on the screens. Handed to
/// <see cref="Services.IScreenQrCodeService"/>, which places it and keeps the screens in step.
/// </summary>
public sealed record ScreenQrCode
{
    /// <summary>
    /// Who is asking, usually a plugin's id. One code per owner: showing a second replaces the
    /// first rather than stacking, so a caller never has to take down what it put up.
    /// </summary>
    public required string OwnerId { get; init; }

    /// <summary>
    /// The picture, already a QR code — a <c>data:</c> URI or an http(s) URL. Raster or SVG:
    /// a provider that renders its own codes server-side (Example returns an SVG) hands back an
    /// image, not a payload, so this contract takes the image and no host or screen needs a QR
    /// library. Nothing here is composed by the screen, the same as <c>ShowImageCommand</c>.
    /// </summary>
    public required string ImageUrl { get; init; }

    /// <summary>
    /// A line under the code. A QR with no words is a mystery, and the screen composes nothing —
    /// this arrives ready to draw.
    /// </summary>
    public string? Caption { get; init; }

    /// <summary>Null takes the venue's corner: it is the venue's screen, and the host knows what else is on it.</summary>
    public ScreenCorner? Corner { get; init; }

    /// <summary>Null takes the venue's size.</summary>
    public ScreenQrSize? Size { get; init; }
}
