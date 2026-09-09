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
    /// What the code says when it is scanned — usually a URL. The host encodes it, so a caller
    /// with a provider that also renders its own codes passes the string rather than the picture:
    /// two codes carrying the same text scan to the same place whatever they look like, and one
    /// we drew ourselves is a vector that stays sharp in a corner a few centimetres across.
    /// </summary>
    public required string Payload { get; init; }

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
