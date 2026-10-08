using QRCoder;

namespace KHost.Domain.Services.QrCodes;

/// <summary>Writes an owner's current code to the user's Downloads folder.</summary>
/// <remarks>Saved by the host rather than handed to the page: the console's own window is a web
/// view that may ignore a browser download, and the console always runs on this machine.</remarks>
public sealed class QrCodePngExporter(IQrCodeService codes, string directory) : IQrCodePngExporter
{
    /// <summary>The Downloads folder in the user's home, which .NET names on no platform.</summary>
    public static string DownloadsDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    // Large enough to print sharp at a few inches without scaling up a blurry bitmap.
    private const int PixelsPerModule = 20;

    public async Task<string?> SaveAsync(string ownerId, string fileStem, CancellationToken cancellationToken = default)
    {
        if (await codes.ReadRegisteredAsync(ownerId) is not { } code)
            return null;

        using var generator = new QRCodeGenerator();

        // M, not the screen's L: a printed code meets glare, folds and smudges a screen never does.
        using var data = generator.CreateQrCode(code.Payload, QRCodeGenerator.ECCLevel.M);

        // The quiet zone is drawn in here, where the screen leaves it to its own margin: a saved image
        // dropped into a design has nothing else around it to give a scanner its border.
        var png = new PngByteQRCode(data).GetGraphic(PixelsPerModule, drawQuietZones: true);

        Directory.CreateDirectory(directory);
        var path = FreePath(Clean(fileStem));

        await File.WriteAllBytesAsync(path, png, cancellationToken);

        return path;
    }

    private string FreePath(string stem)
    {
        var path = Path.Combine(directory, stem + ".png");

        for (var copy = 2; File.Exists(path); copy++)
            path = Path.Combine(directory, $"{stem} ({copy}).png");

        return path;
    }

    // A venue name is the host's free text; a slash or colon in it would otherwise name a folder.
    private static string Clean(string stem)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var cleaned = new string(stem.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();

        return cleaned.Length == 0 ? "QR code" : cleaned;
    }
}
