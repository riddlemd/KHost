using Microsoft.Extensions.Logging.Abstractions;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class MediaFingerprintServiceTests : IDisposable
{
    private const int SampleBytes = 64 * 1024;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-fingerprint-{Guid.NewGuid():N}");
    private readonly MediaFingerprintService _service = new(NullLogger<MediaFingerprintService>.Instance);

    public MediaFingerprintServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void TryGetSize_ReturnsTheLength_ForAnExistingFile()
    {
        var path = WriteFile("a.bin", Pattern(1234));

        Assert.Equal(1234, _service.TryGetSize(path));
    }

    [Fact]
    public void TryGetSize_ReturnsNull_ForAMissingFile()
        => Assert.Null(_service.TryGetSize(Path.Combine(_directory, "nope.bin")));

    [Fact]
    public async Task ComputeFullHashAsync_AgreesForIdenticalBytes_AndDiffersForAnyChange()
    {
        var bytes = Pattern(200_000);
        var a = WriteFile("a.bin", bytes);
        var copy = WriteFile("copy.bin", bytes);

        var changed = Pattern(200_000);
        changed[100_000] ^= 0xFF;
        var b = WriteFile("b.bin", changed);

        Assert.Equal(await _service.ComputeFullHashAsync(a), await _service.ComputeFullHashAsync(copy));
        Assert.NotEqual(await _service.ComputeFullHashAsync(a), await _service.ComputeFullHashAsync(b));
    }

    [Fact]
    public async Task ComputeSampledHashAsync_AgreesForIdenticalBytes()
    {
        var bytes = Pattern(200_000);

        Assert.Equal(
            await _service.ComputeSampledHashAsync(WriteFile("a.bin", bytes)),
            await _service.ComputeSampledHashAsync(WriteFile("copy.bin", bytes)));
    }

    [Fact]
    public async Task ComputeSampledHashAsync_SeesADifferenceInTheHeadOrTail()
    {
        var head = Pattern(200_000);
        head[10] ^= 0xFF;
        var tail = Pattern(200_000);
        tail[^10] ^= 0xFF;

        var original = await _service.ComputeSampledHashAsync(WriteFile("a.bin", Pattern(200_000)));

        Assert.NotEqual(original, await _service.ComputeSampledHashAsync(WriteFile("head.bin", head)));
        Assert.NotEqual(original, await _service.ComputeSampledHashAsync(WriteFile("tail.bin", tail)));
    }

    [Fact]
    public async Task ComputeSampledHashAsync_MissesADifferenceInTheMiddle_WhichIsWhyTheFullHashConfirms()
    {
        var middle = Pattern(200_000);
        middle[100_000] ^= 0xFF;

        var a = WriteFile("a.bin", Pattern(200_000));
        var b = WriteFile("b.bin", middle);

        Assert.Equal(await _service.ComputeSampledHashAsync(a), await _service.ComputeSampledHashAsync(b));
        Assert.NotEqual(await _service.ComputeFullHashAsync(a), await _service.ComputeFullHashAsync(b));
    }

    [Fact]
    public async Task ComputeSampledHashAsync_SeesADifferenceInLength_WhenHeadAndTailAreShared()
    {
        // Both sampled windows are identical bytes, so only the length keeps these apart.
        var a = WriteFile("a.bin", new byte[SampleBytes * 3]);
        var b = WriteFile("b.bin", new byte[SampleBytes * 4]);

        Assert.NotEqual(await _service.ComputeSampledHashAsync(a), await _service.ComputeSampledHashAsync(b));
    }

    [Fact]
    public async Task ComputeSampledHashAsync_HandlesAFileShorterThanOneSample()
    {
        var a = WriteFile("a.bin", Pattern(100));
        var b = WriteFile("b.bin", Pattern(101));

        Assert.NotNull(await _service.ComputeSampledHashAsync(a));
        Assert.NotEqual(await _service.ComputeSampledHashAsync(a), await _service.ComputeSampledHashAsync(b));
    }

    [Fact]
    public async Task ComputeSampledHashAsync_HandlesAnEmptyFile()
        => Assert.NotNull(await _service.ComputeSampledHashAsync(WriteFile("empty.bin", [])));

    [Fact]
    public async Task ComputeHashesAsync_ReturnNull_ForAMissingFile()
    {
        var missing = Path.Combine(_directory, "nope.bin");

        Assert.Null(await _service.ComputeSampledHashAsync(missing));
        Assert.Null(await _service.ComputeFullHashAsync(missing));
    }

    private string WriteFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Pattern(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
            bytes[i] = (byte)(i * 31 % 251);

        return bytes;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch
        {
        }

        GC.SuppressFinalize(this);
    }
}
