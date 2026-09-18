using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Answers what a file is, for a format whoever wrote it is the only one who can read.
/// </summary>
/// <remarks>ffprobe is the host's answer for everything it understands, and it is simply wrong for a
/// container it does not: a Example <c>.kit</c> reads as "Invalid data found", which is
/// indistinguishable from a file with no tracks and no tags. That silence has cost three separate
/// workarounds (an ownership check off the path, a track probe redirected at the render, and a
/// duration taken from the search result rather than the file), each patching one question rather
/// than the missing answer behind all of them.
///
/// A plugin that ships its own container implements this, and every one of those questions is
/// answered from the file itself, before any render exists.</remarks>
public interface IMediaProbe
{
    /// <summary>Whether this probe understands the file. Answered from the path alone: the host asks
    /// before opening anything, and a probe that read the file to decide would be doing the work
    /// twice for every file it turns out not to own.</summary>
    bool CanProbe(string filePath);

    /// <summary>What the file is, or null when it could not be read after all.</summary>
    /// <remarks>Null and an empty result mean different things and are both useful: null is "I could
    /// not tell", an empty one is "I looked, and there is nothing there".</remarks>
    Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default);
}
