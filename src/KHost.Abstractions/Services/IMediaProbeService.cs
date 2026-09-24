using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Asks whichever probe understands this file, so no caller has to know which that is.
/// </summary>
/// <remarks>The router over every <see cref="IMediaProbe"/>. A plugin supplies a probe by
/// implementing <see cref="IMediaProbe"/>; it may take this to read any file the way the host does.
/// A host singleton, callable from any thread. Announces nothing.</remarks>
public interface IMediaProbeService
{
    /// <summary>What the file is, or null when nobody could read it. A plugin that claims the file
    /// answers; otherwise the host's own reader does.</summary>
    /// <remarks>Null and an empty result differ here the same way they do on
    /// <see cref="IMediaProbe"/>: null is "nobody could tell", an empty one is "it was read, and
    /// there is nothing in it". A plugin that claims a file and then cannot read it answers null
    /// and the host's reader is not asked, since it could only fail on the same container.
    ///
    /// <para>Costs a real read every time. Nothing is remembered between calls: a file swapped on
    /// disk under an unchanged path is a case the importer and the faders both have to get right,
    /// and a remembered answer would be yesterday's.</para></remarks>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/>
    /// fires.</exception>
    Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default);
}
