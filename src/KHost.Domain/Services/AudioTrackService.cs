using FFMpegCore;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using KHost.Common.Media;

namespace KHost.Domain.Services;

/// <summary>
/// Reads track names straight off the file rather than storing them on the row: the answer costs
/// one probe, and a file swapped on disk would otherwise keep whatever was recorded at import.
/// </summary>
public sealed class AudioTrackService(ILogger<AudioTrackService> logger) : IAudioTrackService
{
    public async Task<IReadOnlyList<AudioTrack>> ReadTracksAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return [];

        IMediaAnalysis analysis;

        try
        {
            analysis = await FFProbe.AnalyseAsync(filePath, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Never fatal: the song plays on whatever ffmpeg picks, it just cannot be remixed.
            logger.LogWarning(ex, "Could not probe audio tracks in '{FilePath}'", filePath);
            return [];
        }

        // One stream is nothing to balance, whatever it happens to be called.
        if (analysis.AudioStreams.Count < 2) return [];

        var tracks = new List<AudioTrack>();

        for (var index = 0; index < analysis.AudioStreams.Count; index++)
        {
            var name = NameOf(analysis.AudioStreams[index]);
            var role = AudioTrackRoles.FromTrackName(name);

            if (role is { } known)
                tracks.Add(new AudioTrack(index, known, name!));
        }

        // Without a music track there is nothing to set the voices against, and mixing what is
        // left would drop whatever stream the names failed to describe.
        if (!tracks.Any(t => t.Role == AudioTrackRole.Music)) return [];

        return tracks;
    }

    /// <summary>
    /// MP4 carries the name as a handler rather than a title, and Matroska the other way round,
    /// so a file tagged in either shape answers.
    /// </summary>
    private static string? NameOf(AudioStream stream)
    {
        if (stream.Tags is not { } tags) return null;

        foreach (var key in (string[])["title", "name", "handler_name"])
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;

        return null;
    }
}
