namespace KHost.Domain.Services.Screens;

/// <summary>A QR code someone has offered the screens, with the host's own name for who offered it.</summary>
/// <remarks>Not in Abstractions: OwnerId is stamped from the manifest, never passed by a plugin.</remarks>
public sealed record ScreenQrCode
{
    /// <summary>The plugin's id, stamped by the host. A second registration replaces the first.</summary>
    public required string OwnerId { get; init; }

    /// <summary>What the code says when scanned; host-encoded, so a provider passes a string only.</summary>
    public required string Payload { get; init; }

    /// <summary>A line under the code, already composed: the screen draws it, nothing else.</summary>
    public string? Caption { get; init; }
}
