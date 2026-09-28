using System.IO.Compression;

namespace KHost.Domain.Services;

/// <summary>The checks a downloaded zip passes before anything in it is written to disk.</summary>
/// <remarks>Shared by the plugin installer and the FFmpeg installer, so a rule tightened for one
/// cannot be forgotten in the other.</remarks>
internal static class ZipEntryGuard
{
    /// <summary>Throws unless every entry lands inside <paramref name="destination"/> and the whole
    /// archive expands to no more than <paramref name="maxExpandedBytes"/>.</summary>
    /// <remarks>Every entry is checked, not just those about to be extracted: an archive that tries
    /// to escape is hostile, and nothing else in it is trusted either.</remarks>
    public static void EnsureContained(ZipArchive archive, string destination, long maxExpandedBytes)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        long expanded = 0;

        foreach (var entry in archive.Entries)
        {
            expanded += entry.Length;

            if (expanded > maxExpandedBytes)
                throw new InvalidOperationException("The download expands to more than this host will accept.");

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                continue;

            if (!Path.GetFullPath(Path.Combine(destination, entry.FullName)).StartsWith(root, StringComparison.Ordinal))
                throw new InvalidOperationException($"The download writes outside its folder ('{entry.FullName}').");
        }
    }
}
