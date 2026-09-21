using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <inheritdoc />
public sealed class MediaGateService(
    ILogger<MediaGateService> logger,
    IMediaTagReader tags,
    IEnumerable<IMediaPlaybackGate> gates) : IMediaGateService
{
    private readonly IReadOnlyDictionary<string, IMediaPlaybackGate> _gates = gates
        // One plugin instance is bound under several interfaces, so the same gate can arrive twice.
        .GroupBy(gate => gate.ProviderId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    public async Task<PlaybackGateResult> EvaluateAsync(MediaAction action, Media media, CancellationToken cancellationToken = default)
    {
        if (_gates.Count == 0)
            return PlaybackGateResult.Ok;

        // Guarded here rather than at each call site. The rule is that a provider's bug must not
        // strand the night, and spelling it three times left the worst site without it: an
        // exception escaping the play gate reaches the host with a singer at the microphone.
        try
        {
            return await AskAsync(action, media, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A gate failed deciding {Action} for '{FilePath}'", action, media.FilePath);

            return PlaybackGateResult.Ok;
        }
    }

    private async Task<PlaybackGateResult> AskAsync(MediaAction action, Media media, CancellationToken cancellationToken)
    {
        // Asked by name first, because that answer is free and the tag is not: reading a tag
        // opens the file, and for a provider's own container that means the plugin parsing the
        // whole thing to hand back a marker it hardcodes. A gate that recognises the path already
        // knows the file is its own, so there is nothing a tag could add.
        foreach (var gate in _gates.Values)
        {
            if (gate.Claims(media.FilePath))
                return await gate.CanAsync(action, media, cancellationToken);
        }

        // Nothing recognised the name, so ask what the file says it belongs to. This is what
        // catches a gated render sitting under a name its owner does not claim.
        var key = await tags.ReadTagAsync(media.FilePath, IMediaPlaybackGate.MetadataTag, cancellationToken);

        if (!string.IsNullOrEmpty(key) && _gates.TryGetValue(key, out var tagged))
            return await tagged.CanAsync(action, media, cancellationToken);

        return PlaybackGateResult.Ok;
    }
}
