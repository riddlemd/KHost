using System.Diagnostics;
using System.Globalization;
using KHost.Domain.Services;
using KHost.Domain.Services.VideoEncoding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>One hardware encoder cutting a real stream, and the fall back to libx264 when it cannot
/// start. Each encoder derives its own class.</summary>
/// <remarks>An encoder this machine's GPU cannot run reports Skipped with the reason, never a failure.</remarks>
public abstract class HlsMediaStreamServiceHardwareEncodeTests : IDisposable
{
    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-hwencode-tests-{Guid.NewGuid():n}");

    private readonly StubSelector _selector = new();
    private readonly HlsMediaStreamService _service;

    protected HlsMediaStreamServiceHardwareEncodeTests(VideoEncoderProfile encoder)
    {
        Encoder = encoder;
        _service = new HlsMediaStreamService(
            NullLogger<HlsMediaStreamService>.Instance,
            new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251/",
                WorkingDirectory = _workingDirectory,
            }),
            new PlayableMediaSourceService(NullLogger<PlayableMediaSourceService>.Instance, []),
            _selector);
    }

    protected VideoEncoderProfile Encoder { get; }

    /// <summary>The host's probe accepts it, and a six-second song comes out as three two-second
    /// segments each opening on its only keyframe, which is what lets a player start on any of them.</summary>
    /// <remarks>Gated on starting, not on the probe: broken keyframe arguments make the probe reject
    /// the encoder, which would read as "not supported" on every machine instead of failing.</remarks>
    [SkippableFact]
    public async Task OpenAsync_WhereSupported_CutsTwoSecondSegmentsOpeningOnKeyframes()
    {
        Skip.IfNot(FfmpegIsInstalled(), "ffmpeg is not installed");

        var (opens, whyNot) = await OpensHereAsync();
        Skip.IfNot(opens, $"{Encoder.Codec} is not supported on this machine: {whyNot}");

        var probeLog = new RecordingLogger();
        var probe = new VideoEncoderSelector(probeLog, new FfmpegProcessRunner(), [Encoder]);
        var chosen = await probe.SelectAsync(VideoEncoderPreference.Auto);

        Assert.True(chosen == Encoder,
            $"{Encoder.Codec} starts on this machine, but the host's probe rejects it: {probeLog.ReasonFor(Encoder.Codec)}");

        _selector.Answer = Encoder;
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
                $"-v error -select_streams v -show_entries frame=key_frame -of csv=p=0 \"{segment}\"")).Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(f => f.TrimEnd(','))
                .ToList();

            // Opens on one, and holds no other: a stray keyframe mid-segment is bitrate spent for nothing.
            Assert.Equal("1", frames[0]);
            Assert.Single(frames, f => f == "1");
        }
    }

    /// <summary>An encoder that cannot open costs the song nothing: it plays on libx264, and the
    /// encoder is reported so the next song does not try it again.</summary>
    [SkippableFact]
    public async Task OpenAsync_WhereItCannotStart_PlaysOnSoftwareAndIsReported()
    {
        Skip.IfNot(FfmpegIsInstalled(), "ffmpeg is not installed");
        Skip.If((await OpensHereAsync()).Opens,
            $"{Encoder.Codec} works on this machine, so it cannot stand in for an encoder that fails");

        _selector.Answer = Encoder;
        var source = await CreateSampleAsync(seconds: 4);

        var session = await _service.OpenAsync(source);

        Assert.NotNull(_service.ResolveArtifact(session.Id, "seg_00000.ts"));
        Assert.Equal([Encoder], _selector.Failed);
    }

    /// <summary>Whether it starts with the arguments a song uses, and ffmpeg's first word on why not.</summary>
    /// <remarks>Asked of the machine, not of <c>ffmpeg -encoders</c>: a build lists every encoder it
    /// was compiled with (gyan.dev's Windows build lists all four vendors) whatever the GPU.</remarks>
    private async Task<(bool Opens, string WhyNot)> OpensHereAsync()
    {
        var run = await RunAsync("ffmpeg",
            "-hide_banner -loglevel error -f lavfi -i testsrc2=size=1280x720:rate=30 -t 1"
            + Encoder.Arguments(720, 1) + " -f null -");

        var firstLine = run.Error
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? $"ffmpeg exited {run.ExitCode}";

        return (run.ExitCode == 0, firstLine);
    }

    private static bool FfmpegIsInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;

            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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

    /// <summary>Keeps the probe's own account of why it passed an encoder over.</summary>
    private sealed class RecordingLogger : ILogger<VideoEncoderSelector>
    {
        private readonly List<string> _messages = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _messages.Add(formatter(state, exception));

        public string ReasonFor(string codec)
            => _messages.LastOrDefault(m => m.StartsWith($"Video encoder {codec}:", StringComparison.Ordinal))
                   ?[$"Video encoder {codec}:".Length..].Trim()
               ?? _messages.LastOrDefault()
               ?? "the probe gave no reason";
    }
}
