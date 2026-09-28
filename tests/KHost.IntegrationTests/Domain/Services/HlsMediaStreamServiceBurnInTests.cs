using System.Diagnostics;
using System.Globalization;
using KHost.Abstractions.Models;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>Burns a song's timed words into a real encode and reads them back off the picture.</summary>
public class HlsMediaStreamServiceBurnInTests : IDisposable
{
    /// <summary>Set to a folder to keep the frame the first test reads, for a person to look at.</summary>
    private const string FrameFolderVariable = "KHOST_BURNIN_FRAME_DIR";

    private static readonly LyricColor Sung = new(255, 0, 0);
    private static readonly LyricColor Waiting = new(255, 255, 255);

    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-burnin-tests-{Guid.NewGuid():n}");

    private readonly HlsMediaStreamService _service;

    public HlsMediaStreamServiceBurnInTests()
        => _service = new HlsMediaStreamService(
            NullLogger<HlsMediaStreamService>.Instance,
            new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251/",
                WorkingDirectory = _workingDirectory,
            }),
            new PlayableMediaSourceService(NullLogger<PlayableMediaSourceService>.Instance, []));

    /// <summary>A song with no picture of its own: the words go over black, lit as they are sung.</summary>
    [RequiresFfmpegFact]
    public async Task OpenBurningInAsync_PaintsTheSungWordsIntoTheEncodedPicture()
    {
        var source = await CreateToneAsync(seconds: 6);

        var session = await _service.OpenBurningInAsync(source, TimeSpan.Zero, 0, 0, null, Words(6));

        Assert.NotNull(_service.ResolveArtifact(session.Id, "seg_00000.ts"));
        var playlist = await WaitForCompletePlaylistAsync(session.Id);

        var sung = await ExtractFrameAsync(playlist, 3.0, "burned-in-sung.png");
        var waiting = await ExtractFrameAsync(playlist, 0.5, "burned-in-waiting.png");

        // The line's box is 40..600 x 120..240 in the lyrics' 640x360, so 80..1200 x 240..480 at 720p.
        var region = new SKRectI(80, 240, 1200, 480);
        Assert.True(Count(sung, region, IsSung) > 2000, "no sung colour where the line is, once it was sung");
        Assert.Equal(0, Count(sung, Outside(region), IsSung));
        Assert.Equal(0, Count(waiting, region, IsSung));
        Assert.True(Count(waiting, region, IsWaiting) > 2000, "the line was not on screen ahead of being sung");

        Keep(sung, "burned-in-sung.png");
    }

    /// <summary>Closing the session ends the painting and the encode with it, however much song is left.</summary>
    [RequiresFfmpegFact]
    public async Task CloseAsync_StopsTheBurnedInEncodeAndLeavesNoProcess()
    {
        var source = await CreateToneAsync(seconds: 120);

        var session = await _service.OpenBurningInAsync(source, TimeSpan.Zero, 0, 0, null, Words(120));
        var encoder = await _service.EncoderProcessIdAsync(session.Id);
        Assert.True(encoder is { } running && IsRunning(running), "no encode was running to stop");

        await _service.CloseAsync(session.Id);

        for (var i = 0; i < 40 && IsRunning(encoder!.Value); i++) await Task.Delay(50);
        Assert.False(IsRunning(encoder!.Value), "the encode outlived its session");
        Assert.False(Directory.Exists(Path.Combine(_workingDirectory, session.Id)));
    }

    /// <summary>An MP3's cover art never goes under the words: the burn-in paints over black.</summary>
    [RequiresFfmpegFact]
    public async Task OpenBurningInAsync_AnAudioFileWithCoverArt_PaintsOverBlack()
    {
        var source = await CreateToneWithCoverAsync(seconds: 4);

        var session = await _service.OpenBurningInAsync(source, TimeSpan.Zero, 0, 0, null, Words(4));
        var playlist = await WaitForCompletePlaylistAsync(session.Id);
        var frame = await ExtractFrameAsync(playlist, 1.0, "cover-burned-in.png");

        // The cover is solid red; any of it would fill the frame around the words' band.
        var region = new SKRectI(80, 240, 1200, 480);
        Assert.Equal(0, Count(frame, Outside(region), c => c.Red > 40 || c.Green > 40 || c.Blue > 40));
    }

    /// <summary>Even a genuine moving picture inside an audio file stays out from under the words.</summary>
    [RequiresFfmpegFact]
    public async Task OpenBurningInAsync_AnAudioFileWithAMovingPicture_PaintsOverBlack()
    {
        var source = await CreateToneWithRedVideoAsync(seconds: 4, extension: ".m4a");

        var session = await _service.OpenBurningInAsync(source, TimeSpan.Zero, 0, 0, null, Words(4));
        var playlist = await WaitForCompletePlaylistAsync(session.Id);
        var frame = await ExtractFrameAsync(playlist, 1.0, "audio-video-burned-in.png");

        var region = new SKRectI(80, 240, 1200, 480);
        Assert.Equal(0, Count(frame, Outside(region), c => c.Red > 40 || c.Green > 40 || c.Blue > 40));
    }

    /// <summary>The same file named as a video keeps its picture, so the file's name is what decided.</summary>
    [RequiresFfmpegFact]
    public async Task OpenBurningInAsync_AVideoWithAMovingPicture_PaintsOverIt()
    {
        var source = await CreateToneWithRedVideoAsync(seconds: 4, extension: ".mp4");

        var session = await _service.OpenBurningInAsync(source, TimeSpan.Zero, 0, 0, null, Words(4));
        var playlist = await WaitForCompletePlaylistAsync(session.Id);
        var frame = await ExtractFrameAsync(playlist, 1.0, "video-burned-in.png");

        Assert.True(Count(frame, new SKRectI(0, 0, 1280, 200), c => c.Red > 200 && c.Green < 70) > 100_000,
            "the video's own red picture is not under the words");
    }

    /// <summary>A display drawing the words itself is sent the MP3's sound and no picture at all.</summary>
    [RequiresFfmpegFact]
    public async Task OpenUnderDrawnWordsAsync_AnAudioFileWithCoverArt_CarriesNoPicture()
    {
        var source = await CreateToneWithCoverAsync(seconds: 4);

        var session = await _service.OpenUnderDrawnWordsAsync(source, TimeSpan.Zero, 0, 0, null);
        var playlist = await WaitForCompletePlaylistAsync(session.Id);

        Assert.Equal(["audio"], await StreamTypesAsync(playlist));
    }

    [RequiresFfmpegFact]
    public async Task OpenUnderDrawnWordsAsync_AnAudioFileWithAMovingPicture_CarriesNoPicture()
    {
        var source = await CreateToneWithRedVideoAsync(seconds: 4, extension: ".m4a");

        var session = await _service.OpenUnderDrawnWordsAsync(source, TimeSpan.Zero, 0, 0, null);

        Assert.Equal(["audio"], await StreamTypesAsync(await WaitForCompletePlaylistAsync(session.Id)));
    }

    [RequiresFfmpegFact]
    public async Task OpenUnderDrawnWordsAsync_AVideo_KeepsItsPicture()
    {
        var source = await CreateToneWithRedVideoAsync(seconds: 4, extension: ".mp4");

        var session = await _service.OpenUnderDrawnWordsAsync(source, TimeSpan.Zero, 0, 0, null);

        Assert.Equal(["audio", "video"], await StreamTypesAsync(await WaitForCompletePlaylistAsync(session.Id)));
    }

    /// <summary>Unchanged for a song with no timed words: ffmpeg's own pick encodes the cover as a
    /// picture, as it always has.</summary>
    [RequiresFfmpegFact]
    public async Task OpenAsync_AnAudioFileWithCoverArtAndNoTimedWords_StillCarriesTheCover()
    {
        var source = await CreateToneWithCoverAsync(seconds: 4);

        var session = await _service.OpenAsync(source);
        var playlist = await WaitForCompletePlaylistAsync(session.Id);

        Assert.Equal(["audio", "video"], await StreamTypesAsync(playlist));
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_workingDirectory, recursive: true); } catch { /* scratch */ }
        GC.SuppressFinalize(this);
    }

    private static TimedLyrics Words(double seconds) => new()
    {
        DurationSeconds = seconds,
        Bounds = new LyricBox(0, 0, 640, 360),
        Pages =
        [
            new LyricPage
            {
                ShowFromSeconds = 0,
                ShowUntilSeconds = seconds,
                Active = Sung,
                Inactive = Waiting,
                Lines = [new LyricLine { Position = new LyricBox(40, 120, 560, 120), Syllables = [new(1, 2, "WWWW")] }],
            },
        ],
    };

    private static bool IsSung(SKColor c) => c.Red > 200 && c.Green < 70 && c.Blue < 70;

    private static bool IsWaiting(SKColor c) => c.Red > 200 && c.Green > 200 && c.Blue > 200;

    private static IEnumerable<SKRectI> Outside(SKRectI region) =>
    [
        new SKRectI(0, 0, 1280, region.Top),
        new SKRectI(0, region.Bottom, 1280, 720),
    ];

    private static int Count(SKBitmap frame, SKRectI region, Func<SKColor, bool> match)
    {
        var count = 0;
        for (var y = region.Top; y < region.Bottom; y++)
            for (var x = region.Left; x < region.Right; x++)
                if (match(frame.GetPixel(x, y))) count++;
        return count;
    }

    private static int Count(SKBitmap frame, IEnumerable<SKRectI> regions, Func<SKColor, bool> match)
        => regions.Sum(region => Count(frame, region, match));

    private static void Keep(SKBitmap frame, string name)
    {
        if (Environment.GetEnvironmentVariable(FrameFolderVariable) is not { Length: > 0 } folder) return;

        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, name));
        frame.Encode(file, SKEncodedImageFormat.Png, 100);
    }

    private async Task<SKBitmap> ExtractFrameAsync(string playlist, double seconds, string name)
    {
        var png = Path.Combine(_workingDirectory, name);
        using var process = Process.Start(new ProcessStartInfo("ffmpeg",
            string.Format(CultureInfo.InvariantCulture,
                "-hide_banner -loglevel error -y -ss {0:F3} -i \"{1}\" -frames:v 1 \"{2}\"", seconds, playlist, png))
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;

        var errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(File.Exists(png), $"no frame at {seconds}s: {errors}");

        return SKBitmap.Decode(png);
    }

    private async Task<string> WaitForCompletePlaylistAsync(string sessionId)
    {
        for (var i = 0; i < 600; i++)
        {
            if (_service.ResolveArtifact(sessionId, "stream.m3u8") is { } path)
            {
                try
                {
                    if ((await File.ReadAllTextAsync(path)).Contains("#EXT-X-ENDLIST", StringComparison.Ordinal)) return path;
                }
                catch (IOException)
                {
                    // ffmpeg is mid-rewrite; the next poll gets a whole file.
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"the burned-in encode never finished for session {sessionId}");
    }

    /// <summary>By id rather than by command line, which only some platforms will read back.</summary>
    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The distinct stream types in the encode's first segment, sorted.</summary>
    /// <remarks>Distinct because ffprobe lists a TS stream twice, once under its program.</remarks>
    private static async Task<string[]> StreamTypesAsync(string playlist)
    {
        var segment = Path.Combine(Path.GetDirectoryName(playlist)!, "seg_00000.ts");
        using var process = Process.Start(new ProcessStartInfo("ffprobe",
            $"-v error -show_entries stream=codec_type -of csv=p=0 \"{segment}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().Order()];
    }

    /// <summary>A tone with an ordinary solid red video stream, not an attached picture.</summary>
    private async Task<string> CreateToneWithRedVideoAsync(int seconds, string extension)
    {
        Directory.CreateDirectory(_workingDirectory);
        var path = Path.Combine(_workingDirectory, $"moving-{Guid.NewGuid():n}{extension}");

        using var process = Process.Start(new ProcessStartInfo("ffmpeg",
            "-hide_banner -loglevel error -y"
            + $" -f lavfi -t {seconds} -i sine=frequency=440:sample_rate=44100"
            + $" -f lavfi -t {seconds} -i color=c=red:s=320x240:r=25"
            + " -map 0:a -map 1:v -c:a aac -c:v libx264 -pix_fmt yuv420p -f mp4"
            + $" \"{path}\"")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;

        var errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(File.Exists(path), $"ffmpeg did not produce the file: {errors}");

        return path;
    }

    /// <summary>An MP3 carrying a solid red cover as an attached picture.</summary>
    private async Task<string> CreateToneWithCoverAsync(int seconds)
    {
        Directory.CreateDirectory(_workingDirectory);
        var path = Path.Combine(_workingDirectory, $"cover-{Guid.NewGuid():n}.mp3");

        using var process = Process.Start(new ProcessStartInfo("ffmpeg",
            "-hide_banner -loglevel error -y"
            + $" -f lavfi -t {seconds} -i sine=frequency=440:sample_rate=44100"
            + " -f lavfi -i color=c=red:s=320x320 -frames:v 1"
            + " -map 0:a -map 1:v -c:a libmp3lame -c:v png -disposition:v attached_pic -id3v2_version 3"
            + $" \"{path}\"")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;

        var errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(File.Exists(path), $"ffmpeg did not produce the MP3: {errors}");

        return path;
    }

    private async Task<string> CreateToneAsync(int seconds)
    {
        Directory.CreateDirectory(_workingDirectory);
        var path = Path.Combine(_workingDirectory, $"tone-{Guid.NewGuid():n}.m4a");

        // Audio alone, like a song whose picture is nothing but its words.
        using var process = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-hide_banner -loglevel error -y -f lavfi -i sine=frequency=440:sample_rate=44100 -t {seconds} -c:a aac \"{path}\"")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;

        await process.WaitForExitAsync();
        Assert.True(File.Exists(path), "ffmpeg did not produce the tone");

        return path;
    }
}
