using KHost.Abstractions.Services;

namespace KHost.Domain.Services.QrCodes;

/// <summary>Holds the QR codes every owner offers; the read side is <see cref="IQrCodeOfferService"/>.</summary>
/// <remarks>Domain-only because it takes an owner id: a plugin able to pass one could register over
/// another owner's code. A plugin offers its own through IPluginContext, which stamps the owner.</remarks>
public interface IQrCodeService : IQrCodeOfferService
{
    /// <summary>Records what an owner offers; whether it is shown is the venue's decision.</summary>
    Task RegisterAsync(QrCodeRegistration code);

    /// <summary>Withdraws what <paramref name="ownerId"/> offered. Unknown owners are not an error.</summary>
    Task UnregisterAsync(string ownerId);
}
