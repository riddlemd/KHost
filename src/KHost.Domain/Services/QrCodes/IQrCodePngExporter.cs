namespace KHost.Domain.Services.QrCodes;

/// <summary>Saves an owner's current QR code as an image a host can print or put in a design.</summary>
/// <remarks>Domain-only for the same reason as <see cref="IQrCodeService"/>: it takes an owner id.</remarks>
public interface IQrCodePngExporter
{
    /// <summary>Writes the code <paramref name="ownerId"/> offers now as a PNG named after
    /// <paramref name="fileStem"/>, never over an existing file.</summary>
    /// <returns>The file written, or null when that owner offers no code right now.</returns>
    Task<string?> SaveAsync(string ownerId, string fileStem, CancellationToken cancellationToken = default);
}
