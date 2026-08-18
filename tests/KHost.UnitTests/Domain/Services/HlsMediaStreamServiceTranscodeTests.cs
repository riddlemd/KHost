using System.Diagnostics;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Drives real ffmpeg, and skips where it is not installed.</summary>
public class HlsMediaStreamServiceTranscodeTests : IDisposable
{
    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-transcode-tests-{Guid.NewGuid():n}");

    private readonly HlsMediaStreamService _service;

    public HlsMediaStreamServiceTranscodeTests()
        => _service = new HlsMediaStreamService(
            NullLogger<HlsMediaStreamService>.Instance,
            Options.Create(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251/",
                WorkingDirectory = _workingDirectory,
            }));

    [RequiresFfmpegFact]
    public async Task OpenAsync_ProducesAPlaylistAndSegmentsTheHostCanServe()
    {
        var source = await CreateSampleAsync(seconds: 4);

        var session = await _service.OpenAsync(source);

        // Trailing slash on the configured base must not double up in the URL.
        Assert.Equal($"http://host:5251/media/{session.Id}/stream.m3u8", session.PlaylistUrl);

        var playlist = await WaitForArtifactAsync(session.Id, "stream.m3u8");
        Assert.NotNull(playlist);
        Assert.Contains("#EXTM3U", await File.ReadAllTextAsync(playlist));

        var segment = await WaitForArtifactAsync(session.Id, "seg_00000.ts");
        Assert.NotNull(segment);
        Assert.True(new FileInfo(segment).Length > 0);
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_DoesNotReturnUntilThePlaylistIsActuallyFetchable()
    {
        var source = await CreateSampleAsync(seconds: 4);

        var session = await _service.OpenAsync(source);

        // A media element treats a 404 playlist as "source not supported" and never retries, so a
        // URL handed out before ffmpeg has written one kills playback outright.
        var playlist = _service.ResolveArtifact(session.Id, "stream.m3u8");
        Assert.NotNull(playlist);
        Assert.Contains(".ts", await File.ReadAllTextAsync(playlist));
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_ThrowsAndCleansUp_WhenTheSourceCannotBeTranscoded()
    {
        Directory.CreateDirectory(_workingDirectory);
        var notMedia = Path.Combine(_workingDirectory, "notmedia.mp4");
        await File.WriteAllTextAsync(notMedia, "this is not a video");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.OpenAsync(notMedia));
    }

    [RequiresFfmpegFact]
    public async Task CloseAsync_StopsTheTranscodeAndDiscardsItsSegments()
    {
        var source = await CreateSampleAsync(seconds: 4);
        var session = await _service.OpenAsync(source);
        Assert.NotNull(await WaitForArtifactAsync(session.Id, "stream.m3u8"));

        await _service.CloseAsync(session.Id);

        Assert.Null(_service.ResolveArtifact(session.Id, "stream.m3u8"));
        Assert.False(Directory.Exists(Path.Combine(_workingDirectory, session.Id)));
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_ServesTwoConcurrentSessionsIndependently()
    {
        var source = await CreateSampleAsync(seconds: 4);

        var first = await _service.OpenAsync(source);
        var second = await _service.OpenAsync(source);

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotNull(await WaitForArtifactAsync(first.Id, "stream.m3u8"));
        Assert.NotNull(await WaitForArtifactAsync(second.Id, "stream.m3u8"));

        // Closing one must not disturb the other — a second screen's stream outlives the first.
        await _service.CloseAsync(first.Id);
        Assert.Null(_service.ResolveArtifact(first.Id, "stream.m3u8"));
        Assert.NotNull(_service.ResolveArtifact(second.Id, "stream.m3u8"));
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_GivesACdgTheAudioFromTheFileBesideIt()
    {
        var cdg = await CreateCdgPairAsync(seconds: 4);

        var session = await _service.OpenAsync(cdg);

        var segment = await WaitForArtifactAsync(session.Id, "seg_00000.ts");
        Assert.NotNull(segment);

        // A .cdg carries only graphics, so without the companion the song plays silent.
        Assert.Contains("audio", await ProbeStreamTypesAsync(segment));
        Assert.Contains("video", await ProbeStreamTypesAsync(segment));
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_StillStreamsACdgWithNoCompanionAudio()
    {
        var cdg = await CreateCdgPairAsync(seconds: 4);
        File.Delete(Path.ChangeExtension(cdg, ".mp3"));

        // Silent, but it must not take the whole stream down with it.
        var session = await _service.OpenAsync(cdg);

        Assert.NotNull(await WaitForArtifactAsync(session.Id, "seg_00000.ts"));
    }

    private async Task<string> ProbeStreamTypesAsync(string path)
    {
        using var process = Process.Start(new ProcessStartInfo("ffprobe",
            $"-hide_banner -v error -show_entries stream=codec_type -of csv \"{path}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    /// <summary>Writes a blank but structurally valid .cdg next to a real .mp3.</summary>
    private async Task<string> CreateCdgPairAsync(int seconds)
    {
        var audio = await CreateSampleAsync(seconds, audioOnly: true);
        var cdg = Path.ChangeExtension(audio, ".cdg");

        // CD+G is 24-byte packets at 300 per second; all-zero packets decode to an empty screen.
        await File.WriteAllBytesAsync(cdg, new byte[24 * 300 * seconds]);

        return cdg;
    }

    private async Task<string?> WaitForArtifactAsync(string sessionId, string fileName)
    {
        for (var i = 0; i < 200; i++)
        {
            var path = _service.ResolveArtifact(sessionId, fileName);
            if (path is not null) return path;
            await Task.Delay(50);
        }

        return null;
    }

    private async Task<string> CreateSampleAsync(int seconds, bool audioOnly = false)
    {
        Directory.CreateDirectory(_workingDirectory);
        var path = Path.Combine(_workingDirectory, audioOnly ? "sample.mp3" : "sample.mp4");

        // Synthetic: no karaoke media is checked in.
        var arguments = audioOnly
            ? $"-hide_banner -loglevel error -y -f lavfi -i sine=frequency=440:sample_rate=44100 "
              + $"-t {seconds} -c:a libmp3lame \"{path}\""
            : $"-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=320x240:rate=15 "
              + $"-f lavfi -i sine=frequency=440:sample_rate=44100 -t {seconds} "
              + $"-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac \"{path}\"";

        using var process = Process.Start(new ProcessStartInfo("ffmpeg", arguments)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;

        await process.WaitForExitAsync();
        Assert.True(File.Exists(path), "ffmpeg did not produce the sample");

        return path;
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_workingDirectory, recursive: true); } catch { /* scratch */ }
        GC.SuppressFinalize(this);
    }
}

/// <summary>xUnit 2 cannot skip at runtime, so the decision is made in the constructor.</summary>
public sealed class RequiresFfmpegFactAttribute : FactAttribute
{
    public RequiresFfmpegFactAttribute()
    {
        if (!FfmpegIsInstalled.Value)
            Skip = "ffmpeg is not installed";
    }

    private static readonly Lazy<bool> FfmpegIsInstalled = new(() =>
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            process!.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    });
}
