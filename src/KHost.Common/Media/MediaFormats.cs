using KHost.Abstractions.Models;

namespace KHost.Common.Media;

/// <summary>
/// Whether a row is a still rather than something that plays. Kept apart from
/// <see cref="KHost.Abstractions.Models.MediaType"/>: type is what the file is, this is how it reaches the screen.
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

    /// <summary>
    /// Records, and the audio half of a karaoke pair. Which of the two a given file is cannot be
    /// read off the extension — see <see cref="IsKaraokeTrack"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> AudioExtensions =
        [".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus", ".wma"];

    /// <summary>
    /// Anything with a picture track. A karaoke video and an ad clip are the same formats, so the
    /// extension cannot tell them apart and the importer is told which it is looking at.
    /// </summary>
    public static readonly IReadOnlyList<string> VideoExtensions =
        [".mp4", ".mkv", ".avi", ".flv", ".mov", ".webm", ".m4v"];

    /// <summary>Stills, as leading-dot extensions. The same set <see cref="IsImage"/> answers for.</summary>
    public static readonly IReadOnlyList<string> ImageExtensions =
        [.. _imageContentTypes.Keys.Select(key => "." + key.ToLowerInvariant()).Order()];

    /// <summary>
    /// The graphics half of a karaoke pair, which is never imported as a row of its own — the
    /// audio beside it is the row, and this is found through it.
    /// </summary>
    public const string KaraokeGraphicsExtension = ".cdg";

    public static bool IsImage(string? format) => ContentTypeFor(format) is not null;

    /// <summary>
    /// Whether the file is a karaoke backing track rather than a record. A .cdg says so outright,
    /// and an audio file with a .cdg beside it is the other half of the same pair — both are
    /// instrumentals with no singer on them, so neither belongs in break music.
    /// </summary>
    public static bool IsKaraokeTrack(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        if (Path.GetExtension(filePath).Equals(".cdg", StringComparison.OrdinalIgnoreCase))
            return true;

        return File.Exists(Path.ChangeExtension(filePath, ".cdg"));
    }

    /// <summary>
    /// What a file is, from its name and what sits beside it. The importer asks rather than
    /// assuming, because it used to assume karaoke for everything it scanned — which was harmless
    /// only while it scanned nothing else.
    /// </summary>
    /// <param name="videoIsKaraoke">
    /// What to call a file with a picture track. Both answers are ordinary: a karaoke video and an
    /// ad clip are the same formats, and nothing in the file says which one a host just pointed at.
    /// The caller knows, because the host told it.
    /// </param>
    public static MediaType TypeForFile(string filePath, bool videoIsKaraoke = true)
    {
        var extension = Path.GetExtension(filePath);

        if (IsImage(extension))
            return MediaType.Image;

        // Asked before the audio check, and of the path rather than the extension: the .cdg beside
        // an .mp3 is what makes that .mp3 a backing track, and no extension can say so alone.
        if (IsKaraokeTrack(filePath))
            return MediaType.Karaoke;

        if (AudioExtensions.Contains(extension.ToLowerInvariant()))
            return MediaType.Audio;

        return videoIsKaraoke ? MediaType.Karaoke : MediaType.Video;
    }

    /// <summary>Null for anything that is not a still, which is also the endpoint's refusal.</summary>
    public static string? ContentTypeFor(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return null;

        return _imageContentTypes.GetValueOrDefault(format.Trim().TrimStart('.'));
    }
}
