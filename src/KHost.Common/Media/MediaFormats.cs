using KHost.Abstractions.Models;

namespace KHost.Common.Media;

/// <summary>Whether a row is a still rather than something that plays.</summary>
/// <remarks>A still opens no encode, so the host clock alone times it.</remarks>
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

    /// <summary>Whether <paramref name="format"/> (an extension, with or without its leading dot) is a still.</summary>
    public static bool IsImage(string? format) => ContentTypeFor(format) is not null;

    /// <summary>Whether this is the graphics half of a pair, which carries no sound of its own.</summary>
    public static bool IsGraphicsOnlyKaraoke(string filePath)
        => Path.GetExtension(filePath).Equals(KaraokeGraphicsExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The audio that belongs to a <c>.cdg</c>, or null when it is not beside it.</summary>
    /// <remarks>The graphics carry the words and nothing else, so a <c>.cdg</c> without this is half
    /// a song. One helper because four places used to ask this question and two of them disagreed:
    /// <see cref="IsKaraokeTrack"/> counted any audio file beside a <c>.cdg</c> as the pair's other
    /// half, while the players only ever looked for <c>.mp3</c> — so a <c>.cdg</c> next to a
    /// <c>.wav</c> was excluded from import as "part of a pair" and then played silent.
    ///
    /// <para>Matched without regard to case, and by directory listing rather than by
    /// <c>File.Exists</c> on a built name: a case-sensitive filesystem has <c>SONG.CDG</c> and
    /// <c>SONG.MP3</c> as a pair that no exact-case lookup ever finds.</para></remarks>
    public static string? FindKaraokeAudio(string graphicsPath)
    {
        if (string.IsNullOrWhiteSpace(graphicsPath)) return null;

        var directory = Path.GetDirectoryName(graphicsPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;

        var stem = Path.GetFileNameWithoutExtension(graphicsPath);

        foreach (var candidate in Directory.EnumerateFiles(directory))
        {
            if (!Path.GetFileNameWithoutExtension(candidate).Equals(stem, StringComparison.OrdinalIgnoreCase))
                continue;

            var extension = Path.GetExtension(candidate).ToLowerInvariant();

            if (AudioExtensions.Contains(extension)) return candidate;
        }

        return null;
    }

    /// <summary>The graphics that belong to an audio file, or null when none is beside it.</summary>
    /// <remarks>Mirror of <see cref="FindKaraokeAudio"/>: matched by directory listing rather than
    /// <c>File.Exists</c> on a built name, so a case-sensitive filesystem still finds SONG.CDG
    /// beside song.mp3.</remarks>
    public static string? FindKaraokeGraphics(string audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath)) return null;

        var directory = Path.GetDirectoryName(audioPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;

        var stem = Path.GetFileNameWithoutExtension(audioPath);

        foreach (var candidate in Directory.EnumerateFiles(directory))
        {
            if (!Path.GetFileNameWithoutExtension(candidate).Equals(stem, StringComparison.OrdinalIgnoreCase))
                continue;

            if (Path.GetExtension(candidate).Equals(KaraokeGraphicsExtension, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    /// <summary>A .cdg says so outright; an audio file with one beside it is the pair's other half.</summary>
    public static bool IsKaraokeTrack(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        if (Path.GetExtension(filePath).Equals(KaraokeGraphicsExtension, StringComparison.OrdinalIgnoreCase))
            return true;

        return FindKaraokeGraphics(filePath) is not null;
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
