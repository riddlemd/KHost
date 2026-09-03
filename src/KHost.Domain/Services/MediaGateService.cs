using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.Domain.Services;

/// <inheritdoc />
public sealed class MediaGateService(IMediaTagReader tags, IEnumerable<IMediaPlaybackGate> gates) : IMediaGateService
{
    private readonly IReadOnlyDictionary<string, IMediaPlaybackGate> _gates = gates
        .GroupBy(gate => gate.ProviderId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    public async Task<PlaybackGateResult> EvaluateAsync(Media media, CancellationToken cancellationToken = default)
    {
        if (_gates.Count == 0)
            return PlaybackGateResult.Ok;

        var key = await tags.ReadTagAsync(media.FilePath, IMediaPlaybackGate.MetadataTag, cancellationToken);
        if (string.IsNullOrEmpty(key) || !_gates.TryGetValue(key, out var gate))
            return PlaybackGateResult.Ok;

        return await gate.CanPlayAsync(media, cancellationToken);
    }
}
