using KHost.Abstractions.Models;

namespace KHost.Common.Media;

/// <summary>Whether a row is a still rather than something that plays.</summary>
/// <remarks>A still opens no transcode, so the host clock alone times it.</remarks>
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

    /// <summary>Records, and the audio half of a karaoke pair; not readable from the extension.</summary>
    /// <remarks>See <see cref="IsKaraokeTrack"/> for how the two are told apart.</remarks>
    public static readonly IReadOnlyList<string> AudioExtensions =
        [".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus", ".wma"];

    /// <summary>Anything with a picture track; a karaoke video and an ad clip are the same formats.</summary>
    /// <remarks>So the importer is told which it is looking at.</remarks>
    public static readonly IReadOnlyList<string> VideoExtensions =
        [".mp4", ".mkv", ".avi", ".flv", ".mov", ".webm", ".m4v"];

    /// <summary>Stills, as leading-dot extensions. The same set <see cref="IsImage"/> answers for.</summary>
    public static readonly IReadOnlyList<string> ImageExtensions =
        [.. _imageContentTypes.Keys.Select(key => "." + key.ToLowerInvariant()).Order()];

    /// <summary>The graphics half of a karaoke pair, and the half that becomes the library row.</summary>
    /// <remarks>Its presence is what proves the pair is karaoke: an .mp3 on its own could be
    /// anything. The audio beside it is found from here at play time, never imported separately.
    /// </remarks>
    public const string KaraokeGraphicsExtension = ".cdg";

    public static bool IsImage(string? format) => ContentTypeFor(format) is not null;

    /// <summary>A .cdg says so outright; an audio file with one beside it is the pair's other half.</summary>
    public static bool IsKaraokeTrack(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        if (Path.GetExtension(filePath).Equals(".cdg", StringComparison.OrdinalIgnoreCase))
            return true;

        return File.Exists(Path.ChangeExtension(filePath, ".cdg"));
    }

    /// <summary>What a file is, from its name and what sits beside it: asked, not assumed.</summary>
    /// <param name="videoIsKaraoke">What to call a file with a picture track; a karaoke video and an ad clip are the same formats, so the caller has to say which.</param>
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
