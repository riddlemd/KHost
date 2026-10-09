using System.Diagnostics;
using FFMpegCore;
using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.IntegrationTests.Domain.Services.Displays.LocalScreen;

/// <summary>Real ffprobe and ffmpeg: a video the screen cannot play comes out as picture-only H.264
/// in MP4, and one it can is served untouched.</summary>
public class VideoBackdropServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-backdrop-it-{Guid.NewGuid():n}");
    private readonly VideoBackdropService _service;

    public VideoBackdropServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _service = new VideoBackdropService(
            NullLogger<VideoBackdropService>.Instance,
            new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions { BaseAddress = "http://host:5251" }),
            new FfprobeVideoBackdropProbe(NullLogger<FfprobeVideoBackdropProbe>.Instance),
            new FfmpegVideoBackdropEncoder(NullLogger<FfmpegVideoBackdropEncoder>.Instance, new PathFFmpeg()),
            Path.Combine(_directory, "work"));
    }

    [RequiresFfmpegFact]
    public async Task UrlForAsync_Mpeg4InMatroskaWithSound_EncodesToPictureOnlyH264Mp4()
    {
        var source = await ClipAsync("clip.mkv", "-c:v", "mpeg4", "-c:a", "libopus");

        var url = await _service.UrlForAsync(new Media { Title = "Clip", FilePath = source, Type = MediaType.Video });

        Assert.StartsWith("http://host:5251/media/backdrops/", url);
        var served = _service.ResolveFile(url![(url!.LastIndexOf('/') + 1)..]);
        Assert.NotNull(served);
        Assert.NotEqual(source, served);

        var analysis = await FFProbe.AnalyseAsync(served);
        Assert.Contains("mp4", analysis.Format.FormatName);
        Assert.Equal("h264", analysis.PrimaryVideoStream?.CodecName);
        Assert.Equal("yuv420p", analysis.PrimaryVideoStream?.PixelFormat);
        Assert.Empty(analysis.AudioStreams);
        Assert.Equal((1280, 720), (analysis.PrimaryVideoStream!.Width, analysis.PrimaryVideoStream.Height));
    }

    [RequiresFfmpegFact]
    public async Task UrlForAsync_H264AacInMp4_ServesTheFileItself()
    {
        var source = await ClipAsync("clip.mp4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac");

        var url = await _service.UrlForAsync(new Media { Title = "Clip", FilePath = source, Type = MediaType.Video });

        Assert.Equal(source, _service.ResolveFile(url![(url!.LastIndexOf('/') + 1)..]));
    }

    /// <summary>H.264 the web view may not decode: 10-bit is encoded down to 8-bit 4:2:0.</summary>
    [RequiresFfmpegFact]
    public async Task UrlForAsync_TenBitH264InMp4_EncodesToEightBit()
    {
        var source = await ClipAsync("clip.mp4", "-c:v", "libx264", "-pix_fmt", "yuv420p10le", "-c:a", "aac");

        var url = await _service.UrlForAsync(new Media { Title = "Clip", FilePath = source, Type = MediaType.Video });

        var served = _service.ResolveFile(url![(url!.LastIndexOf('/') + 1)..]);
        Assert.NotNull(served);
        Assert.NotEqual(source, served);
        Assert.Equal("yuv420p", (await FFProbe.AnalyseAsync(served)).PrimaryVideoStream?.PixelFormat);
    }

    /// <summary>A 1920x1080 source with sound: two seconds of test pattern and a tone.</summary>
    private async Task<string> ClipAsync(string name, params string[] codecs)
    {
        var path = Path.Combine(_directory, name);
        var start = new ProcessStartInfo("ffmpeg")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        foreach (var argument in (string[])[
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc=size=1920x1080:rate=25:duration=2",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            .. codecs, path])
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"ffmpeg failed building the clip:\n{error}");

        return path;
    }

    public void Dispose()
    {
        _service.Dispose();

        try { Directory.Delete(_directory, recursive: true); }
        catch { /* swept by the OS */ }

        GC.SuppressFinalize(this);
    }
}
