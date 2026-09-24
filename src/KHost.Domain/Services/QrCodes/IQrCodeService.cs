namespace KHost.Domain.Services.QrCodes;

/// <summary>Holds the QR codes every owner offers and answers which one the venue wants up.</summary>
/// <remarks>Takes an owner id, so a plugin could not register over another owner's code.</remarks>
public interface IQrCodeService
{
    /// <summary>Records what an owner offers; whether it is shown is the venue's decision.</summary>
    Task RegisterAsync(QrCodeRegistration code);

    /// <summary>Withdraws what <paramref name="ownerId"/> offered. Unknown owners are not an error.</summary>
    Task UnregisterAsync(string ownerId);

    /// <summary>The venue's chosen code as it should be shown now, or null for nothing.</summary>
    Task<QrCodeOffer?> ReadOfferAsync(CancellationToken cancellationToken = default);
}
