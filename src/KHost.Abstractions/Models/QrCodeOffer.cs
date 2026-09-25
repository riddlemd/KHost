namespace KHost.Abstractions.Models;

/// <summary>The one QR code the venue wants up now, and where the venue wants it.</summary>
/// <remarks>Data only: how it is encoded, and what fills a placement the venue left unset, is the
/// display's call. Read through <see cref="KHost.Abstractions.Services.IQrCodeOfferService"/>.
/// Compared by value, so a display can tell an offer that moved from one read again.</remarks>
public sealed record QrCodeOffer
{
    /// <summary>What the code says when scanned; the display encodes it.</summary>
    public required string Payload { get; init; }

    /// <summary>A line under the code, already composed by its owner; draw it as given.</summary>
    public string? Caption { get; init; }

    /// <summary>The corner the venue chose, or null where it never chose one.</summary>
    public ScreenCorner? Corner { get; init; }

    /// <summary>The size the venue chose, or null where it never chose one.</summary>
    public ScreenQrSize? Size { get; init; }

    /// <summary>Modules of white around the code, or null where the venue never chose.</summary>
    public int? SafeZone { get; init; }

    /// <summary>Inset from the corner, as a fraction of the shorter side, or null where the venue
    /// never chose.</summary>
    public double? Offset { get; init; }
}
