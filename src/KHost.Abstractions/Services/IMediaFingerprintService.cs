namespace KHost.Abstractions.Services;

/// <summary>Fingerprints media to recognise a file already in the library after a move or rename.</summary>
/// <remarks>Three tiers of increasing cost, meant to be asked in order: size, then the cheap
/// fingerprint for files of equal size, then the full hash only where the cheap one matches. Values
/// are stored on library rows and compared on later runs, so each must be stable for unchanged
/// content across restarts. Never throws for a missing or unreadable file: every member answers null
/// instead. Host-owned; a plugin may take it, and has nothing to implement. A host singleton,
/// callable from any thread. Announces nothing.</remarks>
public interface IMediaFingerprintService
{
    /// <summary>The file's length in bytes, without reading its content.</summary>
    /// <returns>Null when the file does not exist or cannot be examined.</returns>
    long? TryGetSize(string filePath);

    /// <summary>A cheap fingerprint of the file, for shortlisting likely duplicates.</summary>
    /// <remarks>Equal for identical content, and cheap enough to take for every file of a matching
    /// size. Two different files may share one where <see cref="ComputeFullHashAsync"/> would tell
    /// them apart, so a match means "probably the same", never "the same". Comparable only with
    /// other values from this member.</remarks>
    /// <returns>An opaque string, or null when the file could not be read.</returns>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/> fires;
    /// cancellation is the one failure not answered with null.</exception>
    Task<string?> ComputeSampledHashAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>A fingerprint of the whole content, for confirming that two files are the same.</summary>
    /// <remarks>Reads every byte, so it costs as much as the file is large; ask it only once
    /// <see cref="ComputeSampledHashAsync"/> has matched. Comparable only with other values from this
    /// member.</remarks>
    /// <returns>An opaque string, or null when the file could not be read.</returns>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/>
    /// fires.</exception>
    Task<string?> ComputeFullHashAsync(string filePath, CancellationToken cancellationToken = default);
}
