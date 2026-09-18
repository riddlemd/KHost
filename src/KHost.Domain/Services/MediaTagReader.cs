using KHost.Abstractions.Services;

namespace KHost.Domain.Services;

/// <inheritdoc />
public sealed class MediaTagReader(IMediaProbeService probes) : IMediaTagReader
{
    public async Task<string?> ReadTagAsync(string filePath, string tag, CancellationToken cancellationToken = default)
        => await probes.ProbeAsync(filePath, cancellationToken) is { } probe
            && probe.Tags.TryGetValue(tag, out var value)
                ? value
                : null;
}
