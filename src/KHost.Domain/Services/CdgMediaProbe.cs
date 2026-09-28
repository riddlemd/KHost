using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Media;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.Domain.Services;

/// <summary>Describes a CD+G by reading the audio beside it, which is where its facts are.</summary>
/// <remarks>A <c>.cdg</c> carries subcode graphics and nothing else: no duration, no audio stream,
/// nothing ffprobe can say about how long the song is. Its length and its tags belong to the
/// <c>.mp3</c> next to it, and the pair is the media.
///
/// <para>This used to be a special case inside <c>MediaFileParsingService</c>, which redirected the
/// probe to a hand-built <c>.mp3</c> name before asking. That put one format's rule in a service
/// that knows about none of them, and it is the same rule the player and the renderer each kept
/// their own copy of.</para></remarks>
public sealed class CdgMediaProbe(
    [FromKeyedServices(MediaProbeService.FallbackKey)] IMediaProbe fallback) : IMediaProbe
{
    public bool CanProbe(string filePath) => MediaFormats.IsCompactDiscGraphics(filePath);

    public async Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (MediaFormats.IsKaraokeArchive(filePath))
            return await ProbeZippedAsync(filePath, cancellationToken);

        // Empty, not null: null says "I could not tell", and this looked and found the other half
        // missing. A caller deciding whether the pair is importable needs those told apart.
        if (MediaFormats.FindKaraokeAudio(filePath) is not { } audio)
            return new MediaProbeResult();

        return await fallback.ProbeAsync(audio, cancellationToken);
    }

    /// <summary>Reads the audio out of a zipped pair onto disk and describes that.</summary>
    /// <remarks>ffprobe reading from a pipe gives the tags but no duration, so it has to be a file.</remarks>
    private async Task<MediaProbeResult?> ProbeZippedAsync(string zipPath, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"khost-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string audio;

            try
            {
                audio = await KaraokeZip.ExtractAudioAsync(zipPath, directory, cancellationToken: cancellationToken);
            }
            catch (KHostException ex)
            {
                // Null where it could not be read at all; empty where it was read and holds no song.
                return ex.ReferenceCode is KaraokeZip.CorruptCode or KaraokeZip.EncryptedCode
                    ? null
                    : new MediaProbeResult();
            }

            return await fallback.ProbeAsync(audio, cancellationToken);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
