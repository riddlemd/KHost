using FFMpegCore;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <summary>Reads a container's format-level tags with the same probe the rest of playback uses.</summary>
public sealed class MediaTagReader(ILogger<MediaTagReader> logger) : IMediaTagReader
{
    public async Task<string?> ReadTagAsync(string filePath, string tag, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            var analysis = await FFProbe.AnalyseAsync(filePath, cancellationToken: cancellationToken);

            // Format tags are case-preserving but the tag names are effectively case-insensitive
            // across muxers, so match that way rather than trusting one file's casing.
            return analysis.Format.Tags?
                .FirstOrDefault(pair => string.Equals(pair.Key, tag, StringComparison.OrdinalIgnoreCase))
                .Value;
        }
        catch (Exception ex)
        {
            // A file that will not probe is a playback problem, surfaced where the load fails — not
            // a gate the reader should invent. Treated as untagged.
            logger.LogWarning(ex, "Could not read tags from '{FilePath}'", filePath);
            return null;
        }
    }
}
