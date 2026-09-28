using System.IO.Compression;
using KHost.Abstractions.Exceptions;
using KHost.Common.Media;

namespace KHost.Domain.Services;

/// <summary>A CD+G pair shipped as one zip: exactly one <c>.cdg</c> and its audio, flat.</summary>
/// <remarks>The zip is the library row. It is read, never unpacked in place: at play the pair is
/// written into the stream session's directory and swept with it.
///
/// <para>Anything but one flat pair is not a song. <c>__MACOSX/</c> and dot-files are what an
/// archiver adds on its own, so they are passed over; everything else counts.</para>
///
/// <para>An entry's name is only ever compared, never joined to a path. What is written lands under
/// <see cref="ExtractedStem"/>, so an entry named <c>../../x</c> cannot steer a write.</para></remarks>
internal static class KaraokeZip
{
    /// <summary>What an extracted pair is called, whatever the entries were called.</summary>
    public const string ExtractedStem = "karaoke";

    /// <summary>The most the pair may expand to. A long song as <c>.wav</c> is well under it.</summary>
    public const long DefaultMaxExpandedBytes = 512L * 1024 * 1024;

    public const string CorruptCode = "KH-CDG-ZIP-CORRUPT";
    public const string EncryptedCode = "KH-CDG-ZIP-ENCRYPTED";
    public const string ShapeCode = "KH-CDG-ZIP-SHAPE";
    public const string TooLargeCode = "KH-CDG-ZIP-TOO-LARGE";

    /// <summary>Throws a <see cref="KHostException"/> naming why the zip is not one song.</summary>
    public static void Validate(string zipPath, long maxExpandedBytes = DefaultMaxExpandedBytes)
    {
        using var archive = Open(zipPath);
        FindPair(archive, zipPath, maxExpandedBytes);
    }

    /// <summary>Writes the pair into <paramref name="directory"/> and returns the <c>.cdg</c>.</summary>
    /// <remarks>The audio lands beside it under the same stem, so <see cref="MediaFormats.FindKaraokeAudio"/>
    /// finds it by the rule every loose pair uses.</remarks>
    public static async Task<string> ExtractPairAsync(
        string zipPath,
        string directory,
        long maxExpandedBytes = DefaultMaxExpandedBytes,
        CancellationToken cancellationToken = default)
    {
        using var archive = Open(zipPath);
        var (graphics, audio) = FindPair(archive, zipPath, maxExpandedBytes);

        var graphicsPath = Path.Combine(directory, ExtractedStem + MediaFormats.KaraokeGraphicsExtension);
        var audioPath = Path.Combine(directory, ExtractedStem + AudioExtensionOf(audio));

        await ExtractAsync(graphics, graphicsPath, zipPath, cancellationToken);
        await ExtractAsync(audio, audioPath, zipPath, cancellationToken);

        return graphicsPath;
    }

    /// <summary>Writes only the audio into <paramref name="directory"/>; the probe needs nothing else.</summary>
    public static async Task<string> ExtractAudioAsync(
        string zipPath,
        string directory,
        long maxExpandedBytes = DefaultMaxExpandedBytes,
        CancellationToken cancellationToken = default)
    {
        using var archive = Open(zipPath);
        var (_, audio) = FindPair(archive, zipPath, maxExpandedBytes);

        var audioPath = Path.Combine(directory, ExtractedStem + AudioExtensionOf(audio));
        await ExtractAsync(audio, audioPath, zipPath, cancellationToken);

        return audioPath;
    }

    private static ZipArchive Open(string zipPath)
    {
        try
        {
            return ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException ex)
        {
            throw Corrupt(zipPath, ex);
        }
    }

    private static (ZipArchiveEntry Graphics, ZipArchiveEntry Audio) FindPair(
        ZipArchive archive, string zipPath, long maxExpandedBytes)
    {
        var entries = archive.Entries.Where(entry => !IsArchiverClutter(entry.FullName)).ToList();
        var name = Path.GetFileName(zipPath);

        if (entries.Any(entry => entry.IsEncrypted))
        {
            throw new KHostException(
                $"“{name}” is password-protected, so KHost cannot read the song inside.",
                "Re-zip the .cdg and its audio without a password, then import it again.",
                EncryptedCode);
        }

        // A separator anywhere means a folder, which is also how a ../ entry is turned away.
        if (entries.Count != 2 || entries.Any(entry => entry.FullName.IndexOfAny(['/', '\\']) >= 0))
            throw Shape(name);

        var graphics = entries.SingleOrDefault(entry => MediaFormats.IsGraphicsOnlyKaraoke(entry.FullName));
        if (graphics is null)
            throw Shape(name);

        var audioName = MediaFormats.FindKaraokeAudioAmong(
            graphics.FullName, entries.Where(entry => entry != graphics).Select(entry => entry.FullName));
        if (audioName is null)
            throw Shape(name);

        var audio = entries.First(entry => entry.FullName == audioName);

        if (graphics.Length + audio.Length > maxExpandedBytes)
            throw TooLarge(name);

        return (graphics, audio);
    }

    private static bool IsArchiverClutter(string fullName)
    {
        if (fullName.StartsWith("__MACOSX/", StringComparison.Ordinal))
            return true;

        var lastSegment = fullName.TrimEnd('/', '\\');
        lastSegment = lastSegment[(lastSegment.LastIndexOfAny(['/', '\\']) + 1)..];

        return lastSegment.StartsWith('.');
    }

    /// <summary>Lower-cased and already known to be audio, so it is safe to put in a file name.</summary>
    private static string AudioExtensionOf(ZipArchiveEntry audio)
        => Path.GetExtension(audio.FullName).ToLowerInvariant();

    private static async Task ExtractAsync(
        ZipArchiveEntry entry, string destination, string zipPath, CancellationToken cancellationToken)
    {
        try
        {
            // The entry's stream ends at its declared length, so the cap checked against the
            // declared lengths also bounds what is written, whatever the data says.
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            TryDelete(destination);
            throw Corrupt(zipPath, ex);
        }
        catch
        {
            TryDelete(destination);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static KHostException Corrupt(string zipPath, Exception inner) => new(
        $"“{Path.GetFileName(zipPath)}” is damaged, so KHost cannot read the song inside.",
        "Replace the zip with a good copy, then try again.",
        CorruptCode,
        inner);

    private static KHostException Shape(string name) => new(
        $"“{name}” is not one karaoke song: it must hold exactly one .cdg and its audio of the same name, not in a folder.",
        "Re-zip the .cdg and its audio on their own, then import it again.",
        ShapeCode);

    private static KHostException TooLarge(string name) => new(
        $"“{name}” unpacks to far more than one song, so KHost will not open it.",
        "Re-zip the .cdg and its audio on their own, then import it again.",
        TooLargeCode);
}
