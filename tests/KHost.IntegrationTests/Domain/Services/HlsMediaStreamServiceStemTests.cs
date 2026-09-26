using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using KHost.Abstractions.Models;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>A song that arrived as separate stems, mixed by the host into one real encode.</summary>
public partial class HlsMediaStreamServiceStemTests : IDisposable
{
    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-stem-tests-{Guid.NewGuid():n}");

    private readonly HlsMediaStreamService _service;

    public HlsMediaStreamServiceStemTests()
        => _service = new HlsMediaStreamService(
            NullLogger<HlsMediaStreamService>.Instance,
            new TestOptionsMonitor<HlsMediaStreamService.ServiceOptions>(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251",
                WorkingDirectory = _workingDirectory,
            }),
            new PlayableMediaSourceService(NullLogger<PlayableMediaSourceService>.Instance, []));

    /// <summary>Three tones at different pitches, so their powers add: muting one voice drops the mix
    /// by the share that voice carried, and a voice at zero contributes nothing.</summary>
    [RequiresFfmpegFact]
    public async Task OpenStemsAsync_MixesEveryStemAtItsOwnLevel_AndTheStreamEnds()
    {
        var all = await MeanVolumeOfMixAsync(music: 100, lead: 100, backing: 100);
        var leadMuted = await MeanVolumeOfMixAsync(music: 100, lead: 0, backing: 100);
        var musicAlone = await MeanVolumeOfMixAsync(music: 100, lead: 0, backing: 0);

        // Equal tones: two carry twice one's power (+3.0 dB), three carry three times (+4.8 dB).
        Assert.InRange(leadMuted - musicAlone, 2.3, 3.7);
        Assert.InRange(all - musicAlone, 4.0, 5.5);
    }

    /// <summary>Closing the encode also closes the session the stems were written into.</summary>
    [RequiresFfmpegFact]
    public async Task CloseAsync_ClosesTheAdoptedStemSessionToo()
    {
        var (stemSession, stems) = await WriteStemsAsync(100, 100, 100);

        var session = await _service.OpenStemsAsync("/songs/a.song", stems, TimeSpan.Zero, 0, 0, null, null, stemSession);
        await _service.CloseAsync(session.Id);

        Assert.Null(_service.ResolveArtifact(stemSession.Id, "music.ogg"));
        Assert.False(Directory.Exists(stemSession.WorkingDirectory));
    }

    /// <summary>Stems have no picture, so burned-in words bring one: the encode carries video, and
    /// still ends with the song.</summary>
    [RequiresFfmpegFact]
    public async Task OpenStemsAsync_WithWords_EncodesAPictureUnderThem()
    {
        var (stemSession, stems) = await WriteStemsAsync(100, 100, 100);

        var session = await _service.OpenStemsAsync(
            "/songs/a.song", stems, TimeSpan.Zero, 0, 0, Words(4), backgroundPath: null, stemSession);
        var playlist = await WaitForCompletePlaylistAsync(session.Id);

        var output = await RunFfmpegAsync($"-hide_banner -i \"{playlist}\" -f null -");
        Assert.Contains("Video: h264", output);
        Assert.Contains("Audio: aac", output);
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_workingDirectory, recursive: true); } catch { /* scratch */ }
        GC.SuppressFinalize(this);
    }

    private async Task<double> MeanVolumeOfMixAsync(int music, int lead, int backing)
    {
        var (stemSession, stems) = await WriteStemsAsync(music, lead, backing);

        var session = await _service.OpenStemsAsync("/songs/a.song", stems, TimeSpan.Zero, 0, 0, null, null, stemSession);
        var playlist = await WaitForCompletePlaylistAsync(session.Id);

        var output = await RunFfmpegAsync($"-hide_banner -i \"{playlist}\" -af volumedetect -f null -");
        var match = MeanVolume().Match(output);
        Assert.True(match.Success, $"no volumedetect reading: {output}");

        await _service.CloseAsync(session.Id);

        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Three Ogg stems in a session, as a renderer writes them, addressed as it would.</summary>
    private async Task<(MediaStreamSession Session, IReadOnlyList<StemSource> Stems)> WriteStemsAsync(
        int music, int lead, int backing)
    {
        Directory.CreateDirectory(_workingDirectory);
        var anchor = Path.Combine(_workingDirectory, $"song-{Guid.NewGuid():n}.song");
        await File.WriteAllTextAsync(anchor, "");

        var session = await _service.OpenWithoutEncodeAsync(anchor);

        async Task<StemSource> StemAsync(int index, AudioTrackRole role, string name, int frequency, int volume)
        {
            var path = Path.Combine(session.WorkingDirectory!, name);
            await RunFfmpegAsync(
                $"-hide_banner -loglevel error -y -f lavfi -i sine=frequency={frequency}:sample_rate=44100 -t 4"
                + $" -ac 2 -c:a vorbis -strict -2 \"{path}\"");
            Assert.True(File.Exists(path), $"ffmpeg did not produce {name}");

            return new StemSource(index, role, _service.BuildArtifactUrl(session.Id, name), volume);
        }

        return (session,
        [
            await StemAsync(0, AudioTrackRole.Music, "music.ogg", 440, music),
            await StemAsync(1, AudioTrackRole.Lead, "lead.ogg", 660, lead),
            await StemAsync(2, AudioTrackRole.Backing, "backing.ogg", 880, backing),
        ]);
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

        throw new TimeoutException($"the stem encode never finished for session {sessionId}");
    }

    private static async Task<string> RunFfmpegAsync(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("ffmpeg", arguments)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;

        var errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return errors;
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
                Active = new LyricColor(255, 0, 0),
                Inactive = new LyricColor(255, 255, 255),
                Lines = [new LyricLine { Position = new LyricBox(40, 120, 560, 120), Syllables = [new(1, 2, "WWWW")] }],
            },
        ],
    };

    [GeneratedRegex(@"mean_volume:\s*(-?[0-9.]+) dB")]
    private static partial Regex MeanVolume();
}
