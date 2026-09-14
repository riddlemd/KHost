namespace KHost.Domain.Services.Screens;

/// <summary>
/// A QR code someone has offered the screens, with the host's own name for who offered it.
/// </summary>
/// <remarks>
/// Host-internal, and deliberately not in <c>KHost.Abstractions</c>. A plugin reaches the screens
/// through <c>IPluginContext.RegisterQrCodeAsync</c>, which fills <see cref="OwnerId"/> in from the
/// manifest the host loaded — so one plugin cannot register a code under another's name by
/// mistake. A contract carrying an owner the caller chooses would hand that footgun back.
/// </remarks>
public sealed record ScreenQrCode
{
    /// <summary>The plugin's id, stamped by the host. One code per owner: a second replaces the first.</summary>
    public required string OwnerId { get; init; }

    /// <summary>
    /// What the code says when it is scanned — usually a URL. The host encodes it, so a caller
    /// with a provider that also renders its own codes passes the string rather than the picture:
    /// two codes carrying the same text scan to the same place whatever they look like, and one
    /// we drew ourselves is a vector that stays sharp in a corner a few centimetres across.
    /// </summary>
    public required string Payload { get; init; }

    /// <summary>
    /// A line under the code. A QR with no words is a mystery, and the screen composes nothing —
    /// this arrives ready to draw.
    /// </summary>
    public string? Caption { get; init; }
}
