using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.Domain.Services;

/// <inheritdoc />
public sealed class AudioTrackService(IMediaProbeService probes) : IAudioTrackService
{
    public async Task<IReadOnlyList<AudioTrack>> ReadTracksAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        // Policy, not facts: the probe says what is in the file, this says what is worth offering a
        // host. Kept here so one probe can feed the faders, the importer and the gate, which do not
        // want the same rules.
        if (await probes.ProbeAsync(filePath, cancellationToken) is not { } probe)
            return [];

        // One track is nothing to balance, whatever it happens to be called.
        if (probe.AudioTracks.Count < 2) return [];

        // Without a music track there is nothing to set the voices against, and mixing what is
        // left would drop whatever stream the names failed to describe.
        if (!probe.AudioTracks.Any(track => track.Role == AudioTrackRole.Music)) return [];

        return probe.AudioTracks;
    }
}
