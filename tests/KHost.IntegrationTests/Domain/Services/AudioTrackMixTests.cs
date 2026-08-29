using System.Diagnostics;
using System.Globalization;
using KHost.Abstractions.Models;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>
/// Drives real ffprobe and ffmpeg against a file shaped like the karaoke ones: three named audio
/// tracks, ordered instrumental, backing, lead — so anything reading position rather than name
/// puts the singer's own part on the control marked backing.
/// </summary>
public class AudioTrackMixTests : IDisposable
{
    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-mix-tests-{Guid.NewGuid():n}");

    private readonly AudioTrackService _tracks = new(NullLogger<AudioTrackService>.Instance);
    private readonly HlsMediaStreamService _service;

    public AudioTrackMixTests()
        => _service = new HlsMediaStreamService(
            NullLogger<HlsMediaStreamService>.Instance,
            Options.Create(new HlsMediaStreamService.ServiceOptions
            {
                BaseAddress = "http://host:5251",
                WorkingDirectory = _workingDirectory,
            }));

    [RequiresFfmpegFact]
    public async Task ReadTracks_NamesTheRoles_RegardlessOfStreamOrder()
    {
        var source = await CreateMultiTrackAsync();

        var tracks = await _tracks.ReadTracksAsync(source);

        Assert.Equal(
            [
                (0, AudioTrackRole.Music),
                (1, AudioTrackRole.Backing),
                (2, AudioTrackRole.Lead),
            ],
            tracks.Select(t => (t.Index, t.Role)));
    }

    [RequiresFfmpegFact]
    public async Task ReadTracks_FindsNothingInAnOrdinarySingleTrackFile()
    {
        var source = await CreateSingleTrackAsync();

        // Most karaoke files are this, and offering faders for one stream would be a lie.
        Assert.Empty(await _tracks.ReadTracksAsync(source));
    }

    [RequiresFfmpegFact]
    public async Task ReadTracks_FindsNothing_WhenTheTracksAreUnnamed()
    {
        var source = await CreateMultiTrackAsync(named: false);

        // Three streams and no way to tell which is which: guessing would mute the wrong voice.
        Assert.Empty(await _tracks.ReadTracksAsync(source));
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_SilencesAVoiceAtZero_AndCarriesItAtFull()
    {
        var source = await CreateMultiTrackAsync();
        var tracks = await _tracks.ReadTracksAsync(source);

        var muted = await LoudnessAsync(source, new AudioMix(tracks, LeadVolume: 0, BackingVolume: 0));
        var full = await LoudnessAsync(source, new AudioMix(tracks, LeadVolume: 100, BackingVolume: 100));

        // The voices are real energy on top of the music, so full has to be measurably louder.
        Assert.True(full > muted + 0.5, $"muted {muted:F1} dB, full {full:F1} dB");
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_AtZeroVoices_MatchesTheMusicAlone()
    {
        var source = await CreateMultiTrackAsync();
        var tracks = await _tracks.ReadTracksAsync(source);

        var mixed = await LoudnessAsync(source, new AudioMix(tracks, 0, 0));
        var musicOnly = await LoudnessAsync(source, mix: null);

        // Zero is a true mute, not a fade: mixing the voices out has to leave the music untouched.
        Assert.True(Math.Abs(mixed - musicOnly) < 0.3, $"mixed {mixed:F1} dB, music alone {musicOnly:F1} dB");
    }

    [RequiresFfmpegFact]
    public async Task OpenAsync_MixesAndTransposesTogether()
    {
        var source = await CreateMultiTrackAsync(seconds: 8);
        var tracks = await _tracks.ReadTracksAsync(source);

        var session = await _service.OpenAsync(
            source, pitch: 2, tempo: -25, mix: new AudioMix(tracks, 50, 50));

        // The graph carries the mix into the pitch and tempo chain; if either half were dropped
        // ffmpeg would fail outright rather than produce a playable stream.
        var playlist = await WaitForCompletePlaylistAsync(session.Id);
        var total = ParseSegmentDurations(playlist).Sum();

        Assert.InRange(total, 9.9, 11.4);
    }

    /// <summary>Mean volume of the first seconds the stream produces, in dBFS.</summary>
    private async Task<double> LoudnessAsync(string source, AudioMix? mix)
    {
        var session = await _service.OpenAsync(source, mix: mix);
        await WaitForCompletePlaylistAsync(session.Id);

        var playlist = _service.ResolveArtifact(session.Id, "stream.m3u8")!;

        using var process = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-hide_banner -nostats -i \"{playlist}\" -map a -af volumedetect -f null -")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await _service.CloseAsync(session.Id);

        var marker = output.IndexOf("mean_volume:", StringComparison.Ordinal);
        Assert.True(marker >= 0, $"ffmpeg reported no mean_volume:\n{output}");

        var text = output[(marker + "mean_volume:".Length)..].Trim();
        return double.Parse(text[..text.IndexOf(' ')], CultureInfo.InvariantCulture);
    }

    private static List<double> ParseSegmentDurations(string playlist) =>
    [
        .. playlist
            .Split('\n')
            .Where(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            .Select(line => double.Parse(
                line["#EXTINF:".Length..].TrimEnd(',', '\r'), CultureInfo.InvariantCulture))
    ];

    private async Task<string> WaitForCompletePlaylistAsync(string sessionId)
    {
        for (var i = 0; i < 400; i++)
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

    /// <summary>
    /// Three tones at distinct pitches so the mix is measurable, tagged the way a real karaoke
    /// file is: MP4 keeps the name on the stream handler, not a title.
    /// </summary>
    private async Task<string> CreateMultiTrackAsync(int seconds = 5, bool named = true)
    {
        Directory.CreateDirectory(_workingDirectory);
        var path = Path.Combine(_workingDirectory, $"tracks-{(named ? "named" : "bare")}.mp4");

        var tags = named
            ? " -metadata:s:a:0 handler_name=Instrumental"
              + " -metadata:s:a:1 handler_name=\"Backing Vocal\""
              + " -metadata:s:a:2 handler_name=\"Lead Vocal\""
            : string.Empty;

        await RunFfmpegAsync(
            "-f lavfi -i testsrc2=size=320x240:rate=30 "
            + "-f lavfi -i sine=frequency=220:sample_rate=44100 "
            + "-f lavfi -i sine=frequency=660:sample_rate=44100 "
            + "-f lavfi -i sine=frequency=440:sample_rate=44100 "
            + $"-t {seconds} -map 0:v -map 1:a -map 2:a -map 3:a{tags} "
            + $"-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac \"{path}\"");

        return path;
    }

    private async Task<string> CreateSingleTrackAsync(int seconds = 4)
    {
        Directory.CreateDirectory(_workingDirectory);
        var path = Path.Combine(_workingDirectory, "single.mp4");

        await RunFfmpegAsync(
            "-f lavfi -i testsrc2=size=320x240:rate=30 "
            + "-f lavfi -i sine=frequency=440:sample_rate=44100 "
            + $"-t {seconds} -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac \"{path}\"");

        return path;
    }

    private static async Task RunFfmpegAsync(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("ffmpeg", $"-hide_banner -loglevel error -y {arguments}")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, $"ffmpeg failed building the sample:\n{error}");
    }

    public void Dispose()
    {
        _service.Dispose();

        try { Directory.Delete(_workingDirectory, recursive: true); }
        catch { /* swept by the OS */ }

        GC.SuppressFinalize(this);
    }
}
