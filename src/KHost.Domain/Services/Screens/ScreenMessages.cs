using KHost.Abstractions.Services.IPC;

namespace KHost.Domain.Services.Screens;

/// <summary>An owner registered or withdrew a QR code.</summary>
/// <remarks>Domain-side, beside <see cref="IScreenQrCodeService"/>: the owner registry is out of a
/// plugin's reach, so the news of it moving is too.</remarks>
public sealed record ScreenQrCodesChanged;

/// <summary>A host asked for the next-singer card.</summary>
/// <remarks>Carries the card, unlike a change message: it is a one-shot request with nothing to
/// re-read afterwards, so a display that was not connected when it was asked never draws it.</remarks>
public sealed record NextSingerCardRequested(ShowNextSingerCommand Card);
