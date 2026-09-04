using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>
/// Puts QR codes on the screens and keeps them there — a screen that reconnects mid-show is
/// correct after one command, the same way the marquee is. Ordinary service, not a plugin keyhole:
/// the host puts its own codes up through it too.
/// </summary>
public interface IScreenQrCodeService
{
    /// <summary>
    /// Shows <paramref name="code"/>, replacing whatever that owner had up. A corner holds one
    /// code, so claiming an occupied one takes the previous occupant's down.
    /// </summary>
    Task ShowAsync(ScreenQrCode code);

    /// <summary>Takes down what <paramref name="ownerId"/> put up. Unknown owners are not an error.</summary>
    Task HideAsync(string ownerId);
}
