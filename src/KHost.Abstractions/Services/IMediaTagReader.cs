namespace KHost.Abstractions.Services;

/// <summary>Reads a single container-level metadata tag off a media file.</summary>
/// <remarks>Asks the same probes <see cref="IMediaProbeService"/> does, so a plugin's own container
/// answers for itself. A host singleton, callable from any thread. Announces nothing.</remarks>
public interface IMediaTagReader
{
    /// <summary>The tag's value, or null when the file has no such tag or cannot be probed.</summary>
    /// <remarks>Also null, without opening anything, for a file not yet on disk. Each call is a real
    /// read of the file.</remarks>
    Task<string?> ReadTagAsync(string filePath, string tag, CancellationToken cancellationToken = default);
}
