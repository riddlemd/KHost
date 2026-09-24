using KHost.Abstractions.Models;

namespace KHost.Domain.Services.QrCodes;

/// <summary>The one code the venue wants up now, and where the venue wants it.</summary>
/// <remarks>Data only: how it is encoded and what fills an unset placement is the display's call.
/// A placement is null where the venue never chose, since a never-asked venue stores zero.</remarks>
public sealed record QrCodeOffer
{
    /// <summary>What the code says when scanned.</summary>
    public required string Payload { get; init; }

    /// <summary>A line under the code, already composed by its owner.</summary>
    public string? Caption { get; init; }

    public ScreenCorner? Corner { get; init; }

    public ScreenQrSize? Size { get; init; }

    /// <summary>Modules of white around the code.</summary>
    public int? SafeZone { get; init; }

    /// <summary>Inset from the corner, as a fraction of the shorter side.</summary>
    public double? Offset { get; init; }
}
