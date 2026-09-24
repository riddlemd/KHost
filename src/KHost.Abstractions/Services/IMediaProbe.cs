using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Answers what a file is, for a format whoever wrote it is the only one who can read.
/// </summary>
/// <remarks>The host's own answer covers every format it understands, and is simply wrong for a
/// container it does not: a provider's own reads as "Invalid data found", indistinguishable from a
/// file with no tracks and no tags. A plugin that ships its own container implements this, and its
/// duration, tracks and tags are then answered from the file itself — for the importer, the
/// playback faders and the gate alike.
///
/// <para>An extension point: a plugin IMPLEMENTS it and the host discovers it. The host asks the
/// probes in turn and the first whose <see cref="CanProbe"/> is true answers; nothing further is
/// asked, and the host's own reader is asked only when no probe claims the file. The plugin's
/// object is one singleton shared across every extension interface it implements, and is called
/// from any thread.</para>
///
/// <para>Answers facts, not policy: the asking service decides, for instance, that one track is
/// nothing to balance.</para></remarks>
public interface IMediaProbe
{
    /// <summary>Whether this probe understands the file. Answered from the path alone: the host asks
    /// before opening anything, and a probe that read the file to decide would be doing the work
    /// twice for every file it turns out not to own.</summary>
    /// <remarks>A throw here is logged and the probe skipped for that file.</remarks>
    bool CanProbe(string filePath);

    /// <summary>What the file is, or null when it could not be read after all.</summary>
    /// <remarks>Null and an empty result mean different things and are both useful: null is "I could
    /// not tell", an empty one is "I looked, and there is nothing there". Return tracks already
    /// roled; a plugin knows its own stems. A throw is logged and treated as null; the host's own
    /// reader is not asked in its place, since it could only fail on the same container. Asked on
    /// every import, load and gate check, with nothing remembered between calls, so keep it
    /// quick.</remarks>
    Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default);
}
