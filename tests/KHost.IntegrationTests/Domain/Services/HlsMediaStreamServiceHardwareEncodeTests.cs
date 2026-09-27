using System.Diagnostics;
using System.Globalization;
using KHost.Domain.Services;
using KHost.Domain.Services.VideoEncoding;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>A hardware encoder cutting a real stream, and the fall back to libx264 when one cannot.</summary>
public class HlsMediaStreamServiceHardwareEncodeTests : IDisposable
{
    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-hwencode-tests-{Guid.NewGuid():n}");

    private readonly StubSelector _selector = new();
    private readonly HlsMediaStreamService _service;

    public HlsMediaStreamServiceHardwareEncodeTests()
        => _service = new HlsMediaStreamService(
            NullLogger<HlsMediaStreamService>.Instance,
            new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251/",
                WorkingDirectory = _workingDirectory,
            }),
            new PlayableMediaSourceService(NullLogger<PlayableMediaSourceService>.Instance, []),
            _selector);

    /// <summary>The real probe picks it, and a six-second song comes out as three two-second
    /// segments each opening on its only keyframe, which is what lets a player start on any of them.</summary>
    [RequiresVideoToolboxFact]
    public async Task OpenAsync_OnVideoToolbox_CutsTwoSecondSegmentsOpeningOnKeyframes()
    {
        var probe = new VideoEncoderSelector(NullLogger<VideoEncoderSelector>.Instance, new FfmpegProcessRunner());
        Assert.Equal(VideoEncoderProfile.VideoToolbox, await probe.SelectAsync(VideoEncoderPreference.Hardware));

        _selector.Answer = VideoEncoderProfile.VideoToolbox;
        var source = await CreateSampleAsync(seconds: 6);

        var session = await _service.OpenAsync(source);
        var playlist = await WaitForCompletePlaylistAsync(session.Id);

        Assert.Empty(_selector.Failed);

        var durations = playlist.Split('\n')
            .Where(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            .Select(line => double.Parse(line["#EXTINF:".Length..].TrimEnd(',', '\r'), CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(3, durations.Count);
        Assert.All(durations, d => Assert.InRange(d, 1.9, 2.1));

        foreach (var segment in Enumerable.Range(0, 3).Select(i => _service.ResolveArtifact(session.Id, $"seg_{i:00000}.ts")!))
        {
            var frames = (await RunAsync("ffprobe",
                $"-v error -select_streams v -show_entries frame=key_frame -of csv=p=0 \"{segment}\""))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(f => f.TrimEnd(','))
                .ToList();

            // Opens on one, and holds no other: a stray keyframe mid-segment is bitrate spent for nothing.
            Assert.Equal("1", frames[0]);
            Assert.Single(frames, f => f == "1");
        }
    }

    /// <summary>A hardware encoder that cannot open costs the song nothing: it plays on libx264,
    /// and the encoder is reported so the next song does not try it again.</summary>
    [RequiresFfmpegFact]
    public async Task OpenAsync_HardwareEncoderThatCannotStart_PlaysOnSoftwareAndIsReported()
    {
        var listing = await RunAsync("ffmpeg", "-hide_banner -encoders");
        var absent = new[]
            {
                VideoEncoderProfile.Nvenc, VideoEncoderProfile.Amf, VideoEncoderProfile.QuickSync,
                VideoEncoderProfile.MediaFoundation,
            }
            .FirstOrDefault(p => !listing.Contains($" {p.Codec} ", StringComparison.Ordinal));
        Assert.NotNull(absent);

        _selector.Answer = absent;
        var source = await CreateSampleAsync(seconds: 4);

        var session = await _service.OpenAsync(source);

        Assert.NotNull(_service.ResolveArtifact(session.Id, "seg_00000.ts"));
        Assert.Equal([absent], _selector.Failed);
    }

    private async Task<string> WaitForCompletePlaylistAsync(string sessionId)
    {
        for (var i = 0; i < 300; i++)
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

        // 25fps: a hardware encoder's default 12-frame GOP would add keyframes at 0.48s steps.
        await RunAsync("ffmpeg",
            $"-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=1280x720:rate=25 "
            + $"-f lavfi -i sine=frequency=440 -t {seconds} "
            + $"-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac \"{path}\"");
        Assert.True(File.Exists(path), "ffmpeg did not produce the sample");

        return path;
    }

    private static async Task<string> RunAsync(string executable, string arguments)
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
        await error;
        return output;
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_workingDirectory, recursive: true); } catch { /* scratch */ }
        GC.SuppressFinalize(this);
    }

    private sealed class StubSelector : IVideoEncoderSelector
    {
        public VideoEncoderProfile Answer { get; set; } = VideoEncoderProfile.Software;
        public List<VideoEncoderProfile> Failed { get; } = [];

        public Task<VideoEncoderProfile> SelectAsync(VideoEncoderPreference preference, CancellationToken cancellationToken = default)
            => Task.FromResult(Answer);

        public void ReportFailure(VideoEncoderProfile encoder) => Failed.Add(encoder);
    }
}

/// <summary>Runs only on macOS with an ffmpeg that has VideoToolbox; skipped everywhere else.</summary>
public sealed class RequiresVideoToolboxFactAttribute : FactAttribute
{
    public RequiresVideoToolboxFactAttribute()
    {
        if (!Available.Value) Skip = "VideoToolbox needs macOS and an ffmpeg built with it";
    }

    private static readonly Lazy<bool> Available = new(() =>
    {
        if (!OperatingSystem.IsMacOS()) return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-hide_banner -encoders")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Contains(" h264_videotoolbox ", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    });
}
