using KHost.Abstractions.Services.IPC;

namespace KHost.Domain.Services.Screens;

/// <summary>Holds the QR codes every owner offers and shows the venue's chosen one.</summary>
/// <remarks>Takes an owner id, so a plugin could not register over another owner's code.</remarks>
public interface IScreenQrCodeService
{
    /// <summary>Records what an owner offers; whether it reaches a screen is the venue's decision.</summary>
    Task RegisterAsync(ScreenQrCode code);

    /// <summary>Withdraws what <paramref name="ownerId"/> offered. Unknown owners are not an error.</summary>
    Task UnregisterAsync(string ownerId);

    /// <summary>The venue's chosen code as it should be drawn now, or an empty set.</summary>
    Task<SetScreenQrCodesCommand> BuildAsync(CancellationToken cancellationToken = default);
}
