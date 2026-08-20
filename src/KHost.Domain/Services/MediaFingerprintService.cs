using System.Buffers.Binary;
using System.Security.Cryptography;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <summary>
/// SHA-256 rather than a faster non-cryptographic hash: arm64 and modern x64 both carry hardware
/// SHA extensions, so it beats XxHash3 here and needs no argument about collision resistance.
/// </summary>
public class MediaFingerprintService : BaseService, IMediaFingerprintService
{
    // Enough of the file to separate same-size songs. Kept small because on a spinning or network
    // library the open dominates, and CD+G sizes sit on a 96-byte grid, so same-size files are
    // common enough that this tier runs more often than it would for arbitrary media.
    private const int SampleBytes = 64 * 1024;

    public MediaFingerprintService(ILogger<MediaFingerprintService> logger)
        : base(logger)
    {
    }

    public long? TryGetSize(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return info.Exists ? info.Length : null;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not measure {FilePath}", filePath);
            return null;
        }
    }

    public async Task<string?> ComputeSampledHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = OpenRead(filePath);
            var length = stream.Length;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            // The length is part of the hash so that a head and tail shared with a longer file —
            // a truncated or padded copy — does not read as the same sample.
            Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, length);
            hash.AppendData(lengthBytes);

            var buffer = new byte[SampleBytes];
            await AppendChunkAsync(hash, stream, buffer, offset: 0, cancellationToken);

            if (length > SampleBytes)
                await AppendChunkAsync(hash, stream, buffer, length - SampleBytes, cancellationToken);

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogDebug(ex, "Could not sample {FilePath}", filePath);
            return null;
        }
    }

    public async Task<string?> ComputeFullHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = OpenRead(filePath);
            using var sha = SHA256.Create();

            return Convert.ToHexStringLower(await sha.ComputeHashAsync(stream, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogDebug(ex, "Could not hash {FilePath}", filePath);
            return null;
        }
    }

    private static async Task AppendChunkAsync(
        IncrementalHash hash, FileStream stream, byte[] buffer, long offset, CancellationToken cancellationToken)
    {
        stream.Seek(offset, SeekOrigin.Begin);

        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken);
            if (read == 0)
                break;

            filled += read;
        }

        hash.AppendData(buffer.AsSpan(0, filled));
    }

    // FileShare.ReadWrite so a file another process is writing still hashes instead of throwing.
    private static FileStream OpenRead(string filePath)
        => new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, SampleBytes, FileOptions.Asynchronous);
}
