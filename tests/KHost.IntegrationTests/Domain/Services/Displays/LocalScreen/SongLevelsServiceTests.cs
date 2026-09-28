using System.Diagnostics;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.IntegrationTests.Domain.Services.Displays.LocalScreen;

/// <summary>The levels a screen draws the beat from, read by real ffmpeg from a tone whose loud and
/// quiet stretches are known.</summary>
public class SongLevelsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-levels-tests-{Guid.NewGuid():n}");

    private readonly FfmpegSongLevelsService _service = new(
        NullLogger<FfmpegSongLevelsService>.Instance,
        new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions
        {
            BaseAddress = "http://host:5251",
        }));

    public SongLevelsServiceTests() => Directory.CreateDirectory(_directory);

    // Bands: 1 is 50-125Hz, 6 is 2800-5600Hz.
    private const int LowBand = 1;
    private const int HighBand = 6;

    [RequiresFfmpegFact]
    public async Task ReadAsync_ATone_LoudWhereItPlays_SilentWhereItStops_AndOnTheSideItIsOn()
    {
        // 0-2s a 100Hz tone on both sides, 2-4s nothing, 4-6s a 4kHz tone on the left only.
        var source = await ToneAsync("sections.wav",
            "if(lt(t,2),0.5*sin(2*PI*100*t),if(lt(t,4),0,0.5*sin(2*PI*4000*t)))"
            + "|if(lt(t,2),0.5*sin(2*PI*100*t),0)");

        var frames = await ReadAsync([new SongLevelsInput(source)]);

        Assert.InRange(frames.Length, 179, 181);

        foreach (var frame in frames[5..55])
        {
            Assert.True(frame[0][LowBand] > 240 && frame[1][LowBand] > 240, "the bass tone reads loud on both sides");
            Assert.True(frame[0][HighBand] < 60, "a bass tone puts nothing in the treble");
        }

        // Past the last window that still reaches back into the tone.
        foreach (var frame in frames[65..115])
            Assert.All(frame, channel => Assert.All(channel, level => Assert.Equal(0, level)));

        foreach (var frame in frames[125..175])
        {
            Assert.True(frame[0][HighBand] > 240, "the treble tone reads loud on its side");
            Assert.Equal(0, frame[1][HighBand]);
            Assert.True(frame[0][LowBand] < 60, "a treble tone puts nothing in the bass");
        }
    }

    [RequiresFfmpegFact]
    public async Task ReadAsync_Stems_MixesThemAtTheirOwnLevels()
    {
        var bass = await ToneAsync("bass.wav", "0.5*sin(2*PI*100*t)");
        var treble = await ToneAsync("treble.wav", "0.5*sin(2*PI*4000*t)");

        var full = await ReadAsync([new SongLevelsInput(bass), new SongLevelsInput(treble)]);

        // Both parts present at once: a mix, not the first input alone.
        Assert.All(full[5..55], frame =>
        {
            Assert.True(frame[0][LowBand] > 240);
            Assert.True(frame[0][HighBand] > 240);
        });
    }

    [RequiresFfmpegFact]
    public async Task ReadAsync_AFileFfmpegCannotOpen_AnswersNull()
    {
        var broken = Path.Combine(_directory, "broken.mp4");
        await File.WriteAllTextAsync(broken, "not a song");

        var url = _service.Begin([new SongLevelsInput(broken)]);

        Assert.Null(await _service.ReadAsync(url[(url.LastIndexOf('/') + 1)..]));
    }

    [RequiresFfmpegFact]
    public async Task Begin_TheNextSong_DropsTheLastOnesLevels()
    {
        var source = await ToneAsync("tone.wav", "0.5*sin(2*PI*100*t)");

        var first = _service.Begin([new SongLevelsInput(source)]);
        var second = _service.Begin([new SongLevelsInput(source)]);

        Assert.Null(await _service.ReadAsync(first[(first.LastIndexOf('/') + 1)..]));
        Assert.NotNull(await _service.ReadAsync(second[(second.LastIndexOf('/') + 1)..]));
    }

    private async Task<byte[][][]> ReadAsync(IReadOnlyList<SongLevelsInput> inputs)
    {
        var url = _service.Begin(inputs);
        Assert.StartsWith("http://host:5251/media/levels/", url);

        var track = await _service.ReadAsync(url[(url.LastIndexOf('/') + 1)..]);
        Assert.NotNull(track);

        return SongLevels.Decode(track) ?? throw new InvalidOperationException("not a levels track");
    }

    private async Task<string> ToneAsync(string name, string expression)
    {
        var path = Path.Combine(_directory, name);

        using var process = Process.Start(new ProcessStartInfo("ffmpeg")
        {
            ArgumentList =
            {
                "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi",
                "-i", $"aevalsrc='{expression}':s=44100:d=6", path,
            },
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"ffmpeg failed building the tone:\n{error}");

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
