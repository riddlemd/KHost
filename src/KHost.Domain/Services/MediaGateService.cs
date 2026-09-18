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
        var key = await tags.ReadTagAsync(media.FilePath, IMediaPlaybackGate.MetadataTag, cancellationToken);

        if (!string.IsNullOrEmpty(key) && _gates.TryGetValue(key, out var tagged))
            return await tagged.CanAsync(action, media, cancellationToken);

        // No tag is not the same as no owner. A format nothing can open has nowhere to carry one,
        // so a gate is given the chance to recognise the file by name: without this, moving a
        // provider's library row onto its own container silently ungates everything it holds.
        foreach (var gate in _gates.Values)
        {
            if (gate.Claims(media.FilePath))
                return await gate.CanAsync(action, media, cancellationToken);
        }

        return PlaybackGateResult.Ok;
    }
}
