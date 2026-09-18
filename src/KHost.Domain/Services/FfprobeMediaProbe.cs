using FFMpegCore;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Media;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <summary>The host's own probe: everything ffmpeg can open, which is everything but a plugin's
/// private container.</summary>
public sealed class FfprobeMediaProbe(ILogger<FfprobeMediaProbe> logger) : IMediaProbe
{
    /// <summary>Claims every file. It is the fallback, chosen only once no plugin has claimed the
    /// file, so claiming narrowly here would leave unclaimed formats with no probe at all.</summary>
    public bool CanProbe(string filePath) => true;

    public async Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return null;

        IMediaAnalysis analysis;

        try
        {
            analysis = await FFProbe.AnalyseAsync(filePath, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Not fatal anywhere: a file that will not probe still plays on whatever ffmpeg picks,
            // it just cannot be remixed or described. Null rather than empty, so a caller can tell
            // "could not read" from "read it, nothing there".
            logger.LogWarning(ex, "Could not probe '{FilePath}'", filePath);
            return null;
        }

        return new MediaProbeResult
        {
            Duration = analysis.Format.Duration > TimeSpan.Zero ? analysis.Format.Duration : null,
            AudioTracks = TracksIn(analysis),
            Tags = TagsIn(analysis),
        };
    }

    private static IReadOnlyList<AudioTrack> TracksIn(IMediaAnalysis analysis)
    {
        var tracks = new List<AudioTrack>();

        for (var index = 0; index < analysis.AudioStreams.Count; index++)
        {
            var name = NameOf(analysis.AudioStreams[index]);

            // A stream whose name says nothing is left out rather than guessed at: it is the
            // caller's business whether what remains is enough to work with.
            if (AudioTrackRoles.FromTrackName(name) is { } role)
                tracks.Add(new AudioTrack(index, role, name!));
        }

        return tracks;
    }

    private static IReadOnlyDictionary<string, string> TagsIn(IMediaAnalysis analysis)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in analysis.Format.Tags ?? [])
            tags[pair.Key] = pair.Value;

        return tags;
    }

    /// <summary>MP4 carries the name as a handler, Matroska as a title (or the reverse).</summary>
    private static string? NameOf(AudioStream stream)
    {
        if (stream.Tags is not { } tags) return null;

        foreach (var key in (string[])["title", "name", "handler_name"])
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;

        return null;
    }
}
