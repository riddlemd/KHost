using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <inheritdoc />
public sealed class MediaProbeService(
    ILogger<MediaProbeService> logger,
    IEnumerable<IMediaProbe> probes,
    [FromKeyedServices(MediaProbeService.FallbackKey)] IMediaProbe fallback) : IMediaProbeService
{
    /// <summary>Registration key for the probe of last resort.</summary>
    /// <remarks>Keyed so it stays out of <c>IEnumerable&lt;IMediaProbe&gt;</c> altogether: it claims
    /// every file, so reached as one of the plugin probes it would answer for whatever happened to
    /// be registered after it, and the loader decides when a plugin's registrations land.</remarks>
    public const string FallbackKey = "khost.probe.fallback";

    // Belt and braces against the same instance also arriving through the open registration.
    private readonly IReadOnlyList<IMediaProbe> _probes = probes.Where(probe => probe != fallback).ToList();

    public async Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        foreach (var probe in _probes)
        {
            bool claimed;

            try
            {
                claimed = probe.CanProbe(filePath);
            }
            catch (Exception ex)
            {
                // A plugin that throws deciding whether a file is its own must not stop the file
                // being read at all: ffprobe still has an answer for most things.
                logger.LogWarning(ex, "A probe failed deciding whether it owns '{FilePath}'", filePath);
                continue;
            }

            if (!claimed) continue;

            try
            {
                return await probe.ProbeAsync(filePath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Its own format, and it could not read it. Falling through to ffprobe would only
                // produce "Invalid data found", so this is as far as the question goes.
                logger.LogWarning(ex, "The probe that owns '{FilePath}' could not read it", filePath);
                return null;
            }
        }

        return await fallback.ProbeAsync(filePath, cancellationToken);
    }
}
