using System.Diagnostics;
using System.Globalization;
using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays;
using KHost.Domain.Services.VideoEncoding;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>A display with no H.264 (Oriel on stock Chromium) gets a real VP9 + Opus stream in fMP4
/// segments, cut on the clock like the H.264 one.</summary>
/// <remarks>Skipped where ffmpeg lacks libvpx-vp9 or libopus.</remarks>
public sealed class HlsMediaStreamServiceOpenCodecEncodeTests : IDisposable
{
    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-opencodec-tests-{Guid.NewGuid():n}");

    private readonly HlsMediaStreamService _service;

    public HlsMediaStreamServiceOpenCodecEncodeTests()
    {
        _service = new HlsMediaStreamService(
            NullLogger<HlsMediaStreamService>.Instance,
            new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251/",
                WorkingDirectory = _workingDirectory,
            }),
            new PlayableMediaSourceService(NullLogger<PlayableMediaSourceService>.Instance, []),
            TimeProvider.System,
            new PathFFmpeg(),
            new SoftwareOnly(),
            new NoH264());
    }

    [SkippableFact]
    public async Task OpenAsync_DisplayWithNoH264_CutsVp9AndOpusInTwoSecondFmp4Segments()
    {
        Skip.IfNot(await EncodesOpenCodecsAsync(), "ffmpeg is missing, or lacks libvpx-vp9 or libopus");

        var session = await _service.OpenAsync(await CreateSampleAsync(seconds: 6));
        var playlist = await WaitForCompletePlaylistAsync(session.Id);

        Assert.Contains($"#EXT-X-MAP:URI=\"{HlsArguments.InitSegmentFileName}\"", playlist);
        var durations = playlist.Split('\n')
            .Where(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            .Select(line => double.Parse(line["#EXTINF:".Length..].TrimEnd(',', '\r'), CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(3, durations.Count);
        Assert.All(durations, d => Assert.InRange(d, 1.9, 2.1));

        // A media segment alone has no codec parameters, so it is read behind its init segment.
        var init = _service.ResolveArtifact(session.Id, HlsArguments.InitSegmentFileName)!;
        var first = _service.ResolveArtifact(session.Id, "seg_00000.m4s")!;
        var joined = Path.Combine(_workingDirectory, "joined.mp4");
        await File.WriteAllBytesAsync(joined, [.. await File.ReadAllBytesAsync(init), .. await File.ReadAllBytesAsync(first)]);

        var codecs = (await RunAsync("ffprobe", $"-v error -show_entries stream=codec_name -of csv=p=0 \"{joined}\"")).Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["vp9", "opus"], codecs.Order().Reverse());
    }

    private static async Task<bool> EncodesOpenCodecsAsync()
    {
        try
        {
            var encoders = (await RunAsync("ffmpeg", "-hide_banner -encoders")).Output;
            return encoders.Contains(" libvpx-vp9 ", StringComparison.Ordinal)
                   && encoders.Contains(" libopus ", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> WaitForCompletePlaylistAsync(string sessionId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            if (_service.ResolveArtifact(sessionId, "stream.m3u8") is { } path)
            {
                try
                {
                    var text = await File.ReadAllTextAsync(path);
                    if (text.Contains("#EXT-X-ENDLIST", StringComparison.Ordinal)) return text;
                }
                catch (IOException)
                {
                    // ffmpeg is mid-rewrite; the next poll gets a whole file.
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"ffmpeg never finished the playlist for session {sessionId}");
    }

    private async Task<string> CreateSampleAsync(int seconds)
    {
        Directory.CreateDirectory(_workingDirectory);
        var path = Path.Combine(_workingDirectory, "sample.mp4");

        await RunAsync("ffmpeg",
            $"-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=1280x720:rate=25 "
            + $"-f lavfi -i sine=frequency=440 -t {seconds} "
            + $"-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac \"{path}\"");
        Assert.True(File.Exists(path), "ffmpeg did not produce the sample");

        return path;
    }

    private static async Task<(string Output, string Error, int ExitCode)> RunAsync(string executable, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var error = process.StandardError.ReadToEndAsync();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (output, await error, process.ExitCode);
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_workingDirectory, recursive: true); } catch { /* scratch */ }
    }

    private sealed class NoH264 : IDisplayCodecs
    {
        public bool OpenCodecsOnly => true;
    }

    private sealed class SoftwareOnly : IVideoEncoderSelector
    {
        public Task WarmAsync(VideoEncoderPreference preference) => Task.CompletedTask;

        public Task<VideoEncoderProfile> SelectAsync(VideoEncoderPreference preference, CancellationToken cancellationToken = default)
            => Task.FromResult(VideoEncoderProfile.Software);

        public void ReportFailure(VideoEncoderProfile encoder) { }
    }
}
