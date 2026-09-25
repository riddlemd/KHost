using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Reads the QR code the venue wants up now. Read-only: offering a code goes through
/// <see cref="IPluginContext.RegisterQrCodeAsync"/>, which stamps the owner.</summary>
/// <remarks>Host-owned; a plugin has nothing to implement. A display provider that draws QR codes
/// takes it, reads <see cref="ReadOfferAsync"/> on connect, and reads again whenever it hears
/// <see cref="KHost.Abstractions.Messaging.Messages.QrCodeOfferChanged"/>,
/// <see cref="KHost.Abstractions.Messaging.Messages.SelectedVenueChanged"/> or
/// <see cref="KHost.Abstractions.Messaging.Messages.PlaybackChanged"/> — a venue may hide its code
/// while someone sings. A host singleton, callable from any thread.</remarks>
public interface IQrCodeOfferService
{
    /// <summary>The venue's chosen code as it should be shown now, or null to show none.</summary>
    /// <remarks>Null when the venue names no source, when the source it names offers nothing yet,
    /// and while a song plays at a venue that hides its code then. Every other owner's code is
    /// held, never offered. The whole answer each time: a display replaces what it drew.</remarks>
    Task<QrCodeOffer?> ReadOfferAsync(CancellationToken cancellationToken = default);
}
