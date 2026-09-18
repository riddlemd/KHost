namespace KHost.Abstractions.Services;

/// <summary>Turns a file the host cannot play into one it can, when a turn needs it.</summary>
/// <remarks>For a format only the plugin understands. the provider's <c>.kit</c> is stems and a timing
/// document, not a video, so nothing downstream can open it: the plugin renders it and the host
/// plays what comes out. The host asks for this because a turn was queued, never at import, so the
/// playable copy lives as long as the turn does rather than forever.</remarks>
public interface IMediaPreparer
{
    /// <summary>Whether this preparer owns the file. A file nobody claims is played as it is.</summary>
    /// <remarks>Asked of the path, so it must not read the file: this is called for every queued
    /// turn on every reconcile.</remarks>
    bool CanPrepare(string filePath);

    /// <summary>Renders it to <paramref name="destination"/>. False when it could not be made, which
    /// leaves the turn unplayable rather than playing something wrong.</summary>
    /// <remarks>Long: a render is minutes of CPU. The host calls this off the path a host is
    /// waiting on and holds it to one at a time.</remarks>
    Task<bool> PrepareAsync(string filePath, string destination, CancellationToken cancellationToken = default);
}
