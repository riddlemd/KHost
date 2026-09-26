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

        var session = await _service.OpenBurningInAsync(source, TimeSpan.Zero, 0, 0, null, Words(6), null);

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

        var session = await _service.OpenBurningInAsync(source, TimeSpan.Zero, 0, 0, null, Words(120), null);
        Assert.True(await CountProcessesReadingAsync(source) > 0, "no encode was running to stop");

        await _service.CloseAsync(session.Id);

        for (var i = 0; i < 40 && await CountProcessesReadingAsync(source) > 0; i++) await Task.Delay(50);
        Assert.Equal(0, await CountProcessesReadingAsync(source));
        Assert.False(Directory.Exists(Path.Combine(_workingDirectory, session.Id)));
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

    private static async Task<int> CountProcessesReadingAsync(string source)
    {
        using var process = Process.Start(new ProcessStartInfo("pgrep", $"-f \"{source}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
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
