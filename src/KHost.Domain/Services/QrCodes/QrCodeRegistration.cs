namespace KHost.Domain.Services.QrCodes;

/// <summary>A QR code someone has offered, with the host's own name for who offered it.</summary>
/// <remarks>Not in Abstractions: OwnerId is stamped from the manifest, never passed by a plugin.</remarks>
public sealed record QrCodeRegistration
{
    /// <summary>The plugin's id, stamped by the host. A second registration replaces the first.</summary>
    public required string OwnerId { get; init; }

    /// <summary>What the code says when scanned; a display encodes it, so an owner passes a string only.</summary>
    public required string Payload { get; init; }

    /// <summary>A line under the code, already composed: a display draws it, nothing else.</summary>
    public string? Caption { get; init; }
}
