namespace KHost.Abstractions.Models;

/// <summary>
/// Whether a row is a still rather than something that plays. Kept apart from
/// <see cref="MediaKind"/>: kind is what the file is for, this is how it reaches the screen.
/// A still opens no transcode, so the host clock alone decides how long it stays up.
/// </summary>
public static class MediaFormats
{
    private static readonly Dictionary<string, string> _imageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JPG"] = "image/jpeg",
        ["JPEG"] = "image/jpeg",
        ["PNG"] = "image/png",
        ["GIF"] = "image/gif",
        ["WEBP"] = "image/webp",
        ["BMP"] = "image/bmp",
    };

    /// <summary>How long a still stays up when nothing has said otherwise.</summary>
    public static readonly TimeSpan DefaultImageDuration = TimeSpan.FromSeconds(15);

    public static bool IsImage(string? format) => ContentTypeFor(format) is not null;

    /// <summary>Null for anything that is not a still, which is also the endpoint's refusal.</summary>
    public static string? ContentTypeFor(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return null;

        return _imageContentTypes.GetValueOrDefault(format.Trim().TrimStart('.'));
    }
}
