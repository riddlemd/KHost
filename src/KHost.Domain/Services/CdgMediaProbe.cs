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
    public bool CanProbe(string filePath)
        => Path.GetExtension(filePath).Equals(MediaFormats.KaraokeGraphicsExtension, StringComparison.OrdinalIgnoreCase);

    public async Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // Empty, not null: null says "I could not tell", and this looked and found the other half
        // missing. A caller deciding whether the pair is importable needs those told apart.
        if (MediaFormats.FindKaraokeAudio(filePath) is not { } audio)
            return new MediaProbeResult();

        return await fallback.ProbeAsync(audio, cancellationToken);
    }
}
