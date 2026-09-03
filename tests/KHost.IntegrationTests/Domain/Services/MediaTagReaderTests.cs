using System.Diagnostics;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>
/// Drives real ffmpeg and ffprobe: proves the gate marker written with the +use_metadata_tags
/// movflag survives in an mp4 and is read back, the round trip the whole playback gate rests on.
/// </summary>
public class MediaTagReaderTests : IDisposable
{
    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-tag-tests-{Guid.NewGuid():n}");

    private readonly MediaTagReader _reader = new(NullLogger<MediaTagReader>.Instance);

    [RequiresFfmpegFact]
    public async Task ReadTag_ReadsAMarkerWrittenWithUseMetadataTags()
    {
        var path = await CreateMarkedMp4Async("karafun");

        var value = await _reader.ReadTagAsync(path, IMediaPlaybackGate.MetadataTag);

        Assert.Equal("karafun", value);
    }

    [RequiresFfmpegFact]
    public async Task ReadTag_UnmarkedFile_IsNull()
    {
        var path = await CreatePlainMp4Async();

        Assert.Null(await _reader.ReadTagAsync(path, IMediaPlaybackGate.MetadataTag));
    }

    [Fact]
    public async Task ReadTag_MissingFile_IsNull()
        => Assert.Null(await _reader.ReadTagAsync(Path.Combine(_workingDirectory, "nope.mp4"), IMediaPlaybackGate.MetadataTag));

    private async Task<string> CreateMarkedMp4Async(string gateKey)
    {
        Directory.CreateDirectory(_workingDirectory);
        var path = Path.Combine(_workingDirectory, "marked.mp4");

        await RunFfmpegAsync(
            "-f lavfi -i color=c=black:s=64x64:d=1 -movflags +faststart+use_metadata_tags "
            + $"-metadata {IMediaPlaybackGate.MetadataTag}={gateKey} \"{path}\"");

        return path;
    }

    private async Task<string> CreatePlainMp4Async()
    {
        Directory.CreateDirectory(_workingDirectory);
        var path = Path.Combine(_workingDirectory, "plain.mp4");

        await RunFfmpegAsync("-f lavfi -i color=c=black:s=64x64:d=1 -movflags +faststart \"" + path + "\"");

        return path;
    }

    private static async Task RunFfmpegAsync(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("ffmpeg", $"-hide_banner -loglevel error -y {arguments}")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, $"ffmpeg failed building the sample:\n{error}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_workingDirectory, recursive: true); }
        catch { /* swept by the OS */ }

        GC.SuppressFinalize(this);
    }
}
