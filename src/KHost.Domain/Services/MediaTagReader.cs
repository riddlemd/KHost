using KHost.Abstractions.Services;

namespace KHost.Domain.Services;

/// <inheritdoc />
public sealed class MediaTagReader(IMediaProbeService probes) : IMediaTagReader
{
    public async Task<string?> ReadTagAsync(string filePath, string tag, CancellationToken cancellationToken = default)
    {
        // A file that is not there yet carries no tag, and a probe is the one question here that
        // opens it. The gate is asked at enqueue, which for a provider's own download runs before
        // the bytes have landed, so without this every remote pick reads as a failure a host
        // cannot act on.
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        return await probes.ProbeAsync(filePath, cancellationToken) is { } probe
            && probe.Tags.TryGetValue(tag, out var value)
                ? value
                : null;
    }
}
