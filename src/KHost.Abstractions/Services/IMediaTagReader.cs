namespace KHost.Abstractions.Services;

/// <summary>Reads a single container-level metadata tag off a media file.</summary>
public interface IMediaTagReader
{
    /// <summary>The tag's value, or null when the file has no such tag or cannot be probed.</summary>
    Task<string?> ReadTagAsync(string filePath, string tag, CancellationToken cancellationToken = default);
}
