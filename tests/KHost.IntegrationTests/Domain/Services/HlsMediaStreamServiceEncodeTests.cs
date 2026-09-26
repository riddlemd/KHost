using System.Diagnostics;
using System.Globalization;
using KHost.Domain.Services;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>Drives real ffmpeg, and skips where it is not installed.</summary>
public class HlsMediaStreamServiceEncodeTests : IDisposable
{
    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-encode-tests-{Guid.NewGuid():n}");

    private readonly HlsMediaStreamService _service;
    private readonly TestOptionsMonitor<HlsMediaStreamService.ServiceOptions> _options;

    public HlsMediaStreamServiceEncodeTests()
        => _service = new HlsMediaStreamService(
            NullLogger<HlsMediaStreamService>.Instance,
            _options = new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251/",
                WorkingDirectory = _workingDirectory,
            }),
            // The real router with nothing registered: every path resolves to itself, which is
            // what the host does for all but a provider's own container.
            new PlayableMediaSourceService(NullLogger<PlayableMediaSourceService>.Instance, []));

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
    public async Task OpenAsync_ThrowsAndCleansUp_WhenTheSourceCannotBeEncoded()
    {
        Directory.CreateDirectory(_workingDirectory);
        var notMedia = Path.Combine(_workingDirectory, "notmedia.mp4");
        await File.WriteAllTextAsync(notMedia, "this is not a video");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.OpenAsync(notMedia));
    }

    [RequiresFfmpegFact]
    public async Task CloseAsync_StopsTheEncodeAndDiscardsItsSegments()
    {
        var source = await CreateSampleAsync(seconds: 4);
        var session = await _service.OpenAsync(source);
        Assert.NotNull(await WaitForArtifactAsync(session.Id, "stream.m3u8"));

        await _service.CloseAsync(session.Id);

        Assert.Null(_service.ResolveArtifact(session.Id, "stream.m3u8"));
        Assert.False(Directory.Exists(Path.Combine(_workingDirectory, session.Id)));
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_CancelledWhileWaitingForThePlaylist_TearsDownTheOrphanedProcess()
    {
        var source = await CreateSampleAsync(seconds: 4);
        using var cts = new CancellationTokenSource();

        // Shorter than ffmpeg needs to start encoding, so the cancellation lands inside the
        // playlist wait rather than before or after it.
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.OpenAsync(source, cancellationToken: cts.Token));

        // A cancelled caller must not leave ffmpeg running to app exit, nor its scratch directory
        // sitting until CloseAllAsync: this session is torn down the moment the wait is cancelled.
        for (var i = 0; i < 100 && Directory.GetDirectories(_workingDirectory).Length > 0; i++)
            await Task.Delay(50);

        Assert.Empty(Directory.GetDirectories(_workingDirectory));
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

        // Closing one must not disturb the other: a replaced stream is retired while its successor plays.
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
    public async Task OpenAsync_PairsANonMp3NeighbourAsTheCompanion()
    {
        var cdg = await CreateCdgPairAsync(seconds: 4);
        var mp3 = Path.ChangeExtension(cdg, ".mp3");
        File.Move(mp3, Path.ChangeExtension(cdg, ".wav"));

        var session = await _service.OpenAsync(cdg);
        var segment = await WaitForArtifactAsync(session.Id, "seg_00000.ts");

        // Any audio beside a .cdg is its other half; looking only for .mp3 is how a .cdg next to a
        // .wav was once excluded from import as part of a pair and then played silent.
        Assert.NotNull(segment);
        Assert.Contains("audio", await ProbeStreamTypesAsync(segment));
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

    /// <summary>A .cdg decodes a frame only when its graphics change, so its picture stops at the
    /// last change while the audio runs on; a player then stalls rather than ending the song.</summary>
    [RequiresFfmpegFact]
    public async Task OpenAsync_EndsACdgWithItsAudio_WhenTheGraphicsStopEarlyAndRunLong()
    {
        // One graphics change at the start, six seconds of packets, four of audio.
        var cdg = await CreateCdgPairAsync(seconds: 4, graphicsSeconds: 6);

        var session = await _service.OpenAsync(cdg);

        var total = ParseSegmentDurations(await WaitForCompletePlaylistAsync(session.Id)).Sum();
        Assert.InRange(total, 3.0, 5.0);

        var playlist = _service.ResolveArtifact(session.Id, "stream.m3u8")!;
        var video = await ProbeLastTimestampAsync(playlist, 'v');
        var audio = await ProbeLastTimestampAsync(playlist, 'a');
        Assert.InRange(Math.Abs(audio - video), 0, 1.0);
    }

    /// <summary>Whole-pixel scaled into the chosen frame, read live, and still ended by the audio.</summary>
    [RequiresFfmpegFact]
    public async Task OpenAsync_ScalesACdgInto720p_AndEndsItWithItsAudio()
        => await AssertScaledCdgAsync(720, "1280,720");

    [RequiresFfmpegFact]
    public async Task OpenAsync_ScalesACdgInto1080p_AndEndsItWithItsAudio()
        => await AssertScaledCdgAsync(1080, "1920,1080");

    /// <summary>A disc that never draws decodes no frame at all, and used to encode to nothing; the
    /// black canvas under it is the picture instead.</summary>
    [RequiresFfmpegFact]
    public async Task OpenAsync_GivesACdgThatNeverDrawsABlackPictureEndingWithItsAudio()
    {
        var cdg = await CreateCdgPairAsync(seconds: 4, draws: false);

        var session = await _service.OpenAsync(cdg);

        var total = ParseSegmentDurations(await WaitForCompletePlaylistAsync(session.Id)).Sum();
        Assert.InRange(total, 3.0, 5.0);

        var playlist = _service.ResolveArtifact(session.Id, "stream.m3u8")!;
        // Scaling is off by default, so the canvas stays at the native picture size.
        Assert.Equal("300,216", await ProbeFrameSizeAsync(playlist));
        Assert.InRange(Math.Abs(await ProbeLastTimestampAsync(playlist, 'a') - await ProbeLastTimestampAsync(playlist, 'v')), 0, 1.0);
    }

    private async Task AssertScaledCdgAsync(int height, string expectedSize)
    {
        _options.Set(new HlsMediaStreamService.ServiceOptions
        {
            BaseAddress = "http://host:5251/",
            WorkingDirectory = _workingDirectory,
            GraphicsScaleHeight = height,
        });
        var cdg = await CreateCdgPairAsync(seconds: 4, graphicsSeconds: 6);

        var session = await _service.OpenAsync(cdg);

        var total = ParseSegmentDurations(await WaitForCompletePlaylistAsync(session.Id)).Sum();
        Assert.InRange(total, 3.0, 5.0);

        var playlist = _service.ResolveArtifact(session.Id, "stream.m3u8")!;
        Assert.Equal(expectedSize, await ProbeFrameSizeAsync(playlist));
        Assert.InRange(Math.Abs(await ProbeLastTimestampAsync(playlist, 'a') - await ProbeLastTimestampAsync(playlist, 'v')), 0, 1.0);
    }

    private static async Task<string> ProbeFrameSizeAsync(string path)
    {
        using var process = Process.Start(new ProcessStartInfo("ffprobe",
            $"-hide_banner -v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{path}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        // A playlist of TS segments lists the stream once per program; the first is enough.
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_SegmentsAtTheConfiguredLength_WhateverTheSourceFrameRate()
    {
        // The sample is 15fps, where a GOP fixed at 60 frames is a four-second keyframe interval,
        // and the muxer can only cut where a keyframe already is.
        var source = await CreateSampleAsync(seconds: 8);

        var session = await _service.OpenAsync(source);

        var durations = ParseSegmentDurations(await WaitForCompletePlaylistAsync(session.Id));

        Assert.NotEmpty(durations);

        // The last one is whatever is left over; the rest are the interval under test.
        Assert.All(durations.SkipLast(1), d => Assert.InRange(d, 1.5, 2.5));
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_ShiftsPitchWithoutDriftingTheAudioOffThePicture()
    {
        // 48kHz on purpose: asetrate reinterprets whatever rate reaches it, so without a resample
        // in front this source runs 8.8% long and slides behind the lyrics.
        var source = await CreateSampleAsync(seconds: 10, sampleRate: 48000);

        var session = await _service.OpenAsync(source, pitch: 4);

        await WaitForCompletePlaylistAsync(session.Id);
        var playlist = _service.ResolveArtifact(session.Id, "stream.m3u8")!;

        // Segment durations are no use: the muxer cuts on video keyframes, so their sum tracks the
        // picture whatever the audio does. Where the streams end is the drift.
        var video = await ProbeLastTimestampAsync(playlist, 'v');
        var audio = await ProbeLastTimestampAsync(playlist, 'a');

        Assert.InRange(Math.Abs(audio - video), 0, 0.4);
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_ShortensTheSongAtAFasterTempo()
    {
        var source = await CreateSampleAsync(seconds: 10);

        var session = await _service.OpenAsync(source, tempo: 50);

        var total = ParseSegmentDurations(await WaitForCompletePlaylistAsync(session.Id)).Sum();

        // Ten seconds at 1.5x. -af alone would leave the picture running the full ten.
        Assert.InRange(total, 6.3, 7.1);
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_KeepsTheAudioWithThePicture_AtAChangedTempo()
    {
        var source = await CreateSampleAsync(seconds: 10);

        var session = await _service.OpenAsync(source, tempo: -25);

        await WaitForCompletePlaylistAsync(session.Id);
        var playlist = _service.ResolveArtifact(session.Id, "stream.m3u8")!;

        var video = await ProbeLastTimestampAsync(playlist, 'v');
        var audio = await ProbeLastTimestampAsync(playlist, 'a');

        // setpts and atempo have to move by the same factor, or the lyrics slide off the music.
        Assert.InRange(Math.Abs(audio - video), 0, 0.4);
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_EncodesPitchUpAgainstTempoDown()
    {
        var source = await CreateSampleAsync(seconds: 6);

        // The corner where one atempo stage would ask for 0.354: ffmpeg rejects that outright, so
        // without chaining this throws rather than playing slightly wrong.
        var session = await _service.OpenAsync(source, pitch: 6, tempo: -50);

        var total = ParseSegmentDurations(await WaitForCompletePlaylistAsync(session.Id)).Sum();

        Assert.InRange(total, 11.0, 13.0);
    }

    private static List<double> ParseSegmentDurations(string playlist) =>
    [
        .. playlist
            .Split('\n')
            .Where(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            .Select(line => double.Parse(
                line["#EXTINF:".Length..].TrimEnd(',', '\r'), CultureInfo.InvariantCulture))
    ];

    /// <summary>Waits for ENDLIST: a growing event playlist is only whole once ffmpeg has exited.</summary>
    private async Task<string> WaitForCompletePlaylistAsync(string sessionId)
    {
        for (var i = 0; i < 300; i++)
        {
            var path = _service.ResolveArtifact(sessionId, "stream.m3u8");

            if (path is not null)
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

    /// <summary>Where one stream of the finished playlist stops, in presentation time.</summary>
    private static async Task<double> ProbeLastTimestampAsync(string playlistPath, char stream)
    {
        using var process = Process.Start(new ProcessStartInfo("ffprobe",
            $"-hide_banner -v error -select_streams {stream} -show_entries packet=pts_time "
            + $"-of csv=p=0 \"{playlistPath}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        var last = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault()
            ?.TrimEnd(',');

        Assert.False(string.IsNullOrWhiteSpace(last), $"ffprobe reported no '{stream}' packets");

        return double.Parse(last!, CultureInfo.InvariantCulture);
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
    private async Task<string> CreateCdgPairAsync(int seconds, int? graphicsSeconds = null, bool draws = true)
    {
        var audio = await CreateSampleAsync(seconds, audioOnly: true);
        var cdg = Path.ChangeExtension(audio, ".cdg");

        // CD+G is 24-byte packets at 300 per second, and a packet decodes to a frame only when it
        // draws. Opening on a memory preset (0x09, instruction 1) as a real disc does gives exactly
        // one frame: a .cdg that never draws gives none, and a picture held to the audio has
        // nothing to hold.
        var packets = new byte[24 * 300 * (graphicsSeconds ?? seconds)];
        if (draws) (packets[0], packets[1], packets[4]) = (0x09, 1, 3);

        await File.WriteAllBytesAsync(cdg, packets);

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

    private async Task<string> CreateSampleAsync(int seconds, bool audioOnly = false, int sampleRate = 44100)
    {
        Directory.CreateDirectory(_workingDirectory);
        var path = Path.Combine(_workingDirectory, audioOnly ? "sample.mp3" : $"sample-{sampleRate}.mp4");

        // Synthetic: no karaoke media is checked in.
        var arguments = audioOnly
            ? $"-hide_banner -loglevel error -y -f lavfi -i sine=frequency=440:sample_rate={sampleRate} "
              + $"-t {seconds} -c:a libmp3lame \"{path}\""
            : $"-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=320x240:rate=15 "
              + $"-f lavfi -i sine=frequency=440:sample_rate={sampleRate} -t {seconds} "
              + $"-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -ar {sampleRate} \"{path}\"";

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
