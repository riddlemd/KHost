namespace KHost.Abstractions.Services;

/// <summary>
/// Content fingerprints for media files, used to recognise a file already in the library after it
/// has been moved, renamed, or copied to a second folder. Every member returns null rather than
/// throwing when the file cannot be read — an unreadable file is not a duplicate, it is unknown.
/// </summary>
public interface IMediaFingerprintService
{
    /// <summary>Size in bytes, without opening the file.</summary>
    long? TryGetSize(string filePath);

    /// <summary>Hashes the size plus the first and last 64 KB.</summary>
    Task<string?> ComputeSampledHashAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Hashes the whole file.</summary>
    Task<string?> ComputeFullHashAsync(string filePath, CancellationToken cancellationToken = default);
}
