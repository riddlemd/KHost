namespace KHost.Domain.Services.Screens;

/// <summary>
/// Holds the QR codes that have been offered and keeps the screens in step — a screen that
/// reconnects mid-show is correct after one command, the same way the marquee is.
/// </summary>
/// <remarks>
/// <para>
/// Registering a code is not the same as showing one. Every owner may offer one at any time; the
/// venue chooses which single source reaches the screens, and a venue that has chosen none shows
/// none. That choice is the host's, which is why this interface does not live in
/// <c>KHost.Abstractions</c>: it takes an owner id, and a plugin able to pass any owner it liked
/// could register over the top of another's code without either of them noticing.
/// </para>
/// <para>
/// The rule in AGENTS.md putting interfaces in <c>Abstractions</c> is about what a plugin builds
/// against. This one is the opposite — an interface a plugin must not reach — so it sits with its
/// implementation instead.
/// </para>
/// </remarks>
public interface IScreenQrCodeService
{
    /// <summary>
    /// Records <paramref name="code"/> as what its owner is currently offering, replacing whatever
    /// that owner had. Whether it reaches a screen is the venue's decision, not the caller's.
    /// </summary>
    Task RegisterAsync(ScreenQrCode code);

    /// <summary>Withdraws what <paramref name="ownerId"/> offered. Unknown owners are not an error.</summary>
    Task UnregisterAsync(string ownerId);
}
