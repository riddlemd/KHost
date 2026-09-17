namespace KHost.Abstractions.Services;

/// <summary>Fingerprints media to recognise a file already in the library after a move or rename.</summary>
public interface IMediaFingerprintService
{
    /// <summary>Size in bytes, without opening the file.</summary>
    long? TryGetSize(string filePath);

    /// <summary>Hashes the size plus the first and last 64 KB.</summary>
    Task<string?> ComputeSampledHashAsync(string filePath, CancellationToken cancellationToken = default);

    Task<string?> ComputeFullHashAsync(string filePath, CancellationToken cancellationToken = default);
}
