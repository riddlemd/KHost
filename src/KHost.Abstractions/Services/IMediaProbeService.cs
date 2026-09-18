using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Asks whichever probe understands this file, so no caller has to know which that is.
/// </summary>
public interface IMediaProbeService
{
    /// <summary>What the file is, or null when nobody could read it. A plugin that claims the file
    /// answers; otherwise ffprobe does.</summary>
    /// <remarks>Null and an empty result differ here the same way they do on
    /// <see cref="IMediaProbe"/>: null is "nobody could tell", an empty one is "it was read, and
    /// there is nothing in it". A plugin that claims a file and then cannot read it answers null
    /// and ffprobe is not asked, since it could only fail on the same container.</remarks>
    /// <remarks>Costs a real read every time, the same as the ffprobe calls it replaced. Nothing is
    /// cached: a file swapped on disk under an unchanged path is a case the importer and the faders
    /// both have to get right, and a cache keyed on the path would hand back yesterday's answer.
    /// </remarks>
    Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default);
}
