namespace KHost.Domain.Services.QrCodes;

/// <summary>An owner registered or withdrew a QR code, so the offer may have moved.</summary>
/// <remarks>Domain-side, beside <see cref="IQrCodeService"/>: the owner registry is out of a
/// plugin's reach, so the news of it moving is too.</remarks>
public sealed record QrCodeOfferChanged;
